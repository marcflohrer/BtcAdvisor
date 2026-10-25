using BtcAdvisor.Services;
using BtcAdvisor.Services.Conditions;

namespace BtcAdvisor.Tests.Services;

[TestFixture]
public class DistributionTopFilterTests
{
    // ── helpers ───────────────────────────────────────────────────────────

    private static DistributionTopState StateWith(
        decimal? min = null,
        decimal? max = null,
        double? prevGain = null)
        => new()
        {
            MinPriceSinceEntry = min,
            MaxPriceSinceEntry = max,
            PreviousCycleGainPercent = prevGain
        };

    // ── Concept A: Momentum brake ─────────────────────────────────────────

    [Test]
    public void IsMomentumBrake_NoPreviousCycle_ReturnsFalse()
    {
        var state = StateWith(prevGain: null);
        Assert.That(DistributionTopFilter.IsMomentumBrake(100m, 120m, state), Is.False);
    }

    [Test]
    public void IsMomentumBrake_PreviousCycleNegative_ReturnsFalse()
    {
        var state = StateWith(prevGain: -10.0);
        Assert.That(DistributionTopFilter.IsMomentumBrake(100m, 120m, state), Is.False);
    }

    [Test]
    public void IsMomentumBrake_CurrentGainBelowMinThreshold_ReturnsFalse()
    {
        // previous gain 100%, current gain 5% (below MomentumBrakeMinCycleGainPercent)
        var state = StateWith(prevGain: 100.0);
        Assert.That(DistributionTopFilter.IsMomentumBrake(100m, 105m, state), Is.False);
    }

    [Test]
    public void IsMomentumBrake_CurrentGainIsHighFractionOfPrevious_ReturnsFalse()
    {
        // previous gain 100%, current gain 60% → ratio 0.60 > MaxRatio 0.40
        var state = StateWith(prevGain: 100.0);
        Assert.That(DistributionTopFilter.IsMomentumBrake(100m, 160m, state), Is.False);
    }

    [Test]
    public void IsMomentumBrake_CurrentGainIsLowFractionOfPrevious_ReturnsTrue()
    {
        // previous gain 100%, current gain 20% → ratio 0.20 < maxRatio 0.25 → true
        // Explicitly passes maxRatio=0.25 since the default constant is 0.0 (disabled per ADR-006)
        var state = StateWith(prevGain: 100.0);
        Assert.That(DistributionTopFilter.IsMomentumBrake(100m, 120m, state, maxRatio: 0.25), Is.True);
    }

    [Test]
    public void IsMomentumBrake_ExactlyAtRatioThreshold_ReturnsFalse()
    {
        // ratio == 0.40 → not below threshold → false
        var state = StateWith(prevGain: 100.0);
        Assert.That(DistributionTopFilter.IsMomentumBrake(100m, 140m, state), Is.False);
    }

    // ── Concept B: Intermediate trough ───────────────────────────────────

    [Test]
    public void IsIntermediateTrough_NoStateData_ReturnsFalse()
    {
        var state = StateWith();
        Assert.That(DistributionTopFilter.IsIntermediateTrough(100m, 150m, state), Is.False);
    }

    [Test]
    public void IsIntermediateTrough_InsufficientProfit_ReturnsFalse()
    {
        // Only 5% gain, below IntermediateTroughMinCycleGainPercent (10%)
        var state = StateWith(min: 80m, max: 110m);
        Assert.That(DistributionTopFilter.IsIntermediateTrough(100m, 105m, state), Is.False);
    }

    [Test]
    public void IsIntermediateTrough_DrawdownTooSmall_ReturnsFalse()
    {
        // min=95, max=110, drawdown = (110-95)/110 = 13.6% < 15%
        var state = StateWith(min: 95m, max: 110m);
        Assert.That(DistributionTopFilter.IsIntermediateTrough(100m, 108m, state), Is.False);
    }

    [Test]
    public void IsIntermediateTrough_NotRecoveredToPeak_ReturnsFalse()
    {
        // drawdown (100→70)/100 = 30% ✓, but current 85 is 15% below max 100 → not within 5%
        var state = StateWith(min: 70m, max: 100m);
        Assert.That(DistributionTopFilter.IsIntermediateTrough(60m, 85m, state), Is.False);
    }

    [Test]
    public void IsIntermediateTrough_AllConditionsMet_ReturnsTrue()
    {
        // entry=60, max=100, min=70, drawdown=30%, current=97 (3% below max), gain from entry=61%
        // Explicitly passes minDrawdownPercent=10.0, recoveryProximityPercent=5.0 since
        // the default constants are 999.0 (disabled per ADR-006)
        var state = StateWith(min: 70m, max: 100m);
        Assert.That(DistributionTopFilter.IsIntermediateTrough(60m, 97m, state,
            minDrawdownPercent: 10.0, recoveryProximityPercent: 5.0), Is.True);
    }

    // ── DistributionTopState helpers ─────────────────────────────────────

    [Test]
    public void State_Tick_UpdatesMinMax()
    {
        var state = new DistributionTopState();
        state.OnEntry(100m);
        state.Tick(90m);
        state.Tick(110m);
        state.Tick(95m);

        Assert.That(state.MinPriceSinceEntry, Is.EqualTo(90m));
        Assert.That(state.MaxPriceSinceEntry, Is.EqualTo(110m));
    }

    [Test]
    public void State_OnExit_SavesGainAndResetsMinMax()
    {
        var state = new DistributionTopState();
        state.OnEntry(100m);
        state.Tick(120m);
        state.OnExit(100m, 120m);

        Assert.That(state.PreviousCycleGainPercent, Is.EqualTo(20.0).Within(0.001));
        Assert.That(state.MinPriceSinceEntry, Is.Null);
        Assert.That(state.MaxPriceSinceEntry, Is.Null);
    }

    [Test]
    public void State_OnEntry_ResetsMinMax()
    {
        var state = new DistributionTopState();
        state.OnEntry(100m);
        state.Tick(80m);
        state.OnEntry(150m); // new entry after exit
        
        Assert.That(state.MinPriceSinceEntry, Is.EqualTo(150m));
        Assert.That(state.MaxPriceSinceEntry, Is.EqualTo(150m));
    }
}
