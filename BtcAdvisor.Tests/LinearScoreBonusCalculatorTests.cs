using BtcAdvisor.Services;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class LinearScoreBonusCalculatorTests
{
    private static VolumeVolatilityOptions Defaults(bool enabled = true) => new()
    {
        EnableScoreBonus = enabled,
        BbSqueezeRatio = 0.7,
        BbExpansionRatio = 1.3,
        BbExpansionVoteWeight = 1.0,
        BbSqueezeVotePenalty = 1.0,
        ObvBullishSlopeThresholdPercent = 0.5,
        ObvBearishSlopeThresholdPercent = 0.5,
        ObvVoteWeight = 1.5,
        VwapReclaimVoteWeight = 1.5,
        VwapTrendDistancePercent = 1.0,
        VwapTrendVoteWeight = 0.5,
        MfiOversoldThreshold = 20d,
        MfiOverboughtThreshold = 80d,
        MfiVoteWeight = 1.0,
    };

    [Test]
    public void Compute_WhenDisabled_ReturnsZeroes()
    {
        var snap = new MarketContextSnapshot(1.0, 2.0, 5.0);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults(enabled: false));
        Assert.That(b, Is.EqualTo(0d));
        Assert.That(s, Is.EqualTo(0d));
    }

    [Test]
    public void Compute_WhenSnapshotNull_ReturnsZeroes()
    {
        var (b, s) = LinearScoreBonusCalculator.Compute(null, Defaults());
        Assert.That(b, Is.EqualTo(0d));
        Assert.That(s, Is.EqualTo(0d));
    }

    [Test]
    public void Compute_BbSqueeze_PenalisesBoth()
    {
        // width 1.0 vs MA 2.0 → ratio 0.5 < 0.7
        var snap = new MarketContextSnapshot(1.0, 2.0, 0d);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(-1.0));
        Assert.That(s, Is.EqualTo(-1.0));
    }

    [Test]
    public void Compute_BbExpansion_RewardsBoth()
    {
        // width 3.0 vs MA 2.0 → ratio 1.5 > 1.3
        var snap = new MarketContextSnapshot(3.0, 2.0, 0d);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(1.0));
        Assert.That(s, Is.EqualTo(1.0));
    }

    [Test]
    public void Compute_BbInBetween_NoBbContribution()
    {
        var snap = new MarketContextSnapshot(2.0, 2.0, 0d);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(0d));
        Assert.That(s, Is.EqualTo(0d));
    }

    [Test]
    public void Compute_PositiveObvSlope_RewardsBullishOnly()
    {
        var snap = new MarketContextSnapshot(null, null, 1.0);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(1.5));
        Assert.That(s, Is.EqualTo(0d));
    }

    [Test]
    public void Compute_NegativeObvSlope_RewardsBearishOnly()
    {
        var snap = new MarketContextSnapshot(null, null, -1.0);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(0d));
        Assert.That(s, Is.EqualTo(1.5));
    }

    [Test]
    public void Compute_ObvBelowThreshold_NoContribution()
    {
        var snap = new MarketContextSnapshot(null, null, 0.2);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(0d));
        Assert.That(s, Is.EqualTo(0d));
    }

    [Test]
    public void Compute_ExpansionPlusBullishObv_StacksBullish()
    {
        var snap = new MarketContextSnapshot(3.0, 2.0, 1.0);
        var (b, _) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(2.5));
    }

    [Test]
    public void Compute_VwapReclaimed_RewardsBullish()
    {
        var snap = new MarketContextSnapshot(null, null, null, VwapDistancePercent: 0.5, VwapReclaimed: true, Mfi: null);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(1.5));
        Assert.That(s, Is.EqualTo(0d));
    }

    [Test]
    public void Compute_VwapDistanceAboveTrend_AddsTrendBullish()
    {
        var snap = new MarketContextSnapshot(null, null, null, VwapDistancePercent: 2.0, VwapReclaimed: false, Mfi: null);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(0.5));
        Assert.That(s, Is.EqualTo(0d));
    }

    [Test]
    public void Compute_VwapDistanceBelowTrend_AddsTrendBearish()
    {
        var snap = new MarketContextSnapshot(null, null, null, VwapDistancePercent: -2.0, VwapReclaimed: false, Mfi: null);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(0d));
        Assert.That(s, Is.EqualTo(0.5));
    }

    [Test]
    public void Compute_MfiOversold_RewardsBullish()
    {
        var snap = new MarketContextSnapshot(null, null, null, VwapDistancePercent: null, VwapReclaimed: false, Mfi: 15d);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(1.0));
        Assert.That(s, Is.EqualTo(0d));
    }

    [Test]
    public void Compute_MfiOverbought_RewardsBearish()
    {
        var snap = new MarketContextSnapshot(null, null, null, VwapDistancePercent: null, VwapReclaimed: false, Mfi: 85d);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(0d));
        Assert.That(s, Is.EqualTo(1.0));
    }

    [Test]
    public void Compute_MfiNeutral_NoContribution()
    {
        var snap = new MarketContextSnapshot(null, null, null, VwapDistancePercent: null, VwapReclaimed: false, Mfi: 50d);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(0d));
        Assert.That(s, Is.EqualTo(0d));
    }

    [Test]
    public void Compute_FullBullishStack_AccumulatesAllVotes()
    {
        // Expansion (+1) + OBV bullish (+1.5) + VWAP reclaim (+1.5) + VWAP trend (+0.5) + MFI oversold (+1) = 5.5
        var snap = new MarketContextSnapshot(3.0, 2.0, 1.0, VwapDistancePercent: 2.0, VwapReclaimed: true, Mfi: 10d);
        var (b, s) = LinearScoreBonusCalculator.Compute(snap, Defaults());
        Assert.That(b, Is.EqualTo(5.5).Within(1e-9));
        Assert.That(s, Is.EqualTo(1.0));
    }
}
