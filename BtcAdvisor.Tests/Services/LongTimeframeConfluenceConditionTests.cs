using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests.Services;

[TestFixture]
public class LongTimeframeConfluenceConditionTests
{
    // ─── Bullish Confluence ───────────────────────────────────────────────────

    [Test]
    public void BullishConfluence_WhenFourRangesWithBothAnchorsAlign_ReturnsTrueAndBuyAdvice()
    {
        // Requires alignedRanges ≥4 (LongTimeframeThresholds.BullishMinAlignedRanges),
        // alignedAnchorRanges ≥2 (both 1d + 1w bullish), and trendCountSum ≥2.
        var sut = new LongTimeframeBullishConfluenceConditionMet();
        var osci = BuildLongRangeStats(bullishRanges: ["2h", "12h", "1d", "1w"]);
        var ma = BuildLongRangeStats(bullishRanges: ["2h", "12h", "1d", "1w"]);

        var result = sut.IsConditionsMet(osci, ma, null, null, 1m);

        Assert.That(result, Is.True);
        Assert.That(sut.TradeAdvice, Is.EqualTo(TradeAdvice.Buy));
    }

    [Test]
    public void BullishConfluence_WhenOnlyTwoRangesAlign_ReturnsFalse()
    {
        var sut = new LongTimeframeBullishConfluenceConditionMet();
        var osci = BuildLongRangeStats(bullishRanges: ["2h", "4h"]);
        var ma = BuildLongRangeStats(bullishRanges: ["2h", "4h"]);

        var result = sut.IsConditionsMet(osci, ma, null, null, 1m);

        Assert.That(result, Is.False);
    }

    [Test]
    public void BullishConfluence_WithRelaxedAlignedRangeThreshold_ReturnsTrue()
    {
        var thresholds = new LongTimeframeConfluenceThresholdSet(
            bullishMinAlignedRanges: 2,
            bullishMinTrendCountSum: 1);
        var sut = new LongTimeframeBullishConfluenceConditionMet(thresholds);
        var osci = BuildLongRangeStats(bullishRanges: ["1d", "1w"]);
        var ma = BuildLongRangeStats(bullishRanges: ["1d", "1w"]);

        var result = sut.IsConditionsMet(osci, ma, null, null, 1m);

        Assert.That(result, Is.True);
    }

    [Test]
    public void BullishConfluence_WhenOnlyLowerLongRangesAlign_ReturnsFalse()
    {
        // 3 non-anchor ranges aligned (2h, 4h, 12h): alignedRanges=3 <4 → false.
        var sut = new LongTimeframeBullishConfluenceConditionMet();
        var osci = BuildLongRangeStats(bullishRanges: ["2h", "4h", "12h"]);
        var ma = BuildLongRangeStats(bullishRanges: ["2h", "4h", "12h"]);

        var result = sut.IsConditionsMet(osci, ma, null, null, 1m);

        Assert.That(result, Is.False);
    }

    [Test]
    public void BullishConfluence_WhenOscillatorsAlignButMovingAveragesBearish_StillReturnsTrue()
    {
        // Key test: oscillators identify the reversal but MAs remain structurally bearish.
        // Requires 4 aligned + 2 anchors + trendCountSum ≥2 (new thresholds from LongTimeframeThresholds).
        var sut = new LongTimeframeBullishConfluenceConditionMet();
        var osci = BuildLongRangeStats(bullishRanges: ["2h", "12h", "1d", "1w"]);
        var ma = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            ["2h"] = BuildStat(11, 1, false),
            ["4h"] = BuildStat(11, 1, false),
            ["12h"] = BuildStat(11, 1, false),
            ["1d"] = BuildStat(11, 1, false),
            ["1w"] = BuildStat(11, 1, false),
        };

        var result = sut.IsConditionsMet(osci, ma, null, null, 1m);

        Assert.That(result, Is.True,
            "Oscillator-only alignment should ignore MA bearish at market bottoms");
    }

    // ─── Bearish Confluence ───────────────────────────────────────────────────

    [Test]
    public void BearishConfluence_WhenThreeLongRangesAligns_ReturnsTrueAndSellAdvice()
    {
        // Bearish requires alignedRanges ≥3 (no anchor requirement), trendCountSum ≥2.
        var sut = new LongTimeframeBearishConfluenceConditionMet();
        var osci = BuildLongRangeStats(bearishRanges: ["4h", "12h", "1d"]);
        var ma = BuildLongRangeStats(bearishRanges: ["4h", "12h", "1d"]);

        var result = sut.IsConditionsMet(osci, ma, null, null, 1m);

        Assert.That(result, Is.True);
        Assert.That(sut.TradeAdvice, Is.EqualTo(TradeAdvice.Sell));
    }

    [Test]
    public void BearishConfluence_WhenWeeklyAnchorConflicts_ReturnsFalse()
    {
        var sut = new LongTimeframeBearishConfluenceConditionMet();
        var osci = BuildLongRangeStats(
            bullishRanges: ["1w"],
            bearishRanges: ["4h", "12h", "1d"]);
        var ma = BuildLongRangeStats(
            bullishRanges: ["1w"],
            bearishRanges: ["4h", "12h", "1d"]);

        var result = sut.IsConditionsMet(osci, ma, null, null, 1m);

        Assert.That(result, Is.False);
    }

    [Test]
    public void BearishConfluence_WithHigherTrendThreshold_ReturnsFalse()
    {
        var thresholds = new LongTimeframeConfluenceThresholdSet(
            bearishMinTrendCountSum: 4);
        var sut = new LongTimeframeBearishConfluenceConditionMet(thresholds);
        var osci = BuildLongRangeStats(bearishRanges: ["4h", "12h", "1d"]);
        var ma = BuildLongRangeStats(bearishRanges: ["4h", "12h", "1d"]);

        var result = sut.IsConditionsMet(osci, ma, null, null, 1m);

        Assert.That(result, Is.False);
    }

    // ─── Legacy ranges ────────────────────────────────────────────────────────

    [Test]
    public void Confluence_WhenLegacyRangesOnly_ReturnsFalse()
    {
        var bullish = new LongTimeframeBullishConfluenceConditionMet();
        var bearish = new LongTimeframeBearishConfluenceConditionMet();

        var osci = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            ["5m"] = BuildStat(0, 3, true),
            ["15m"] = BuildStat(0, 3, true),
            ["30m"] = BuildStat(0, 3, true),
            ["1h"] = BuildStat(0, 3, true),
            ["2h"] = BuildStat(0, 3, true),
        };

        var ma = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            ["5m"] = BuildStat(0, 7, false),
            ["15m"] = BuildStat(0, 7, false),
            ["30m"] = BuildStat(0, 7, false),
            ["1h"] = BuildStat(0, 7, false),
            ["2h"] = BuildStat(0, 7, false),
        };

        Assert.That(bullish.IsConditionsMet(osci, ma, null, null, 1m), Is.False);
        Assert.That(bearish.IsConditionsMet(osci, ma, null, null, 1m), Is.False);
    }

    // ─── MultiTrend variants ──────────────────────────────────────────────────

    [Test]
    public void MultiTrendBullish_WhenConfluencePassesAndTrendSumMeetsThreshold_ReturnsTrue()
    {
        // 4 aligned ranges (2h, 12h, 1d, 1w), each with 1 medium trend → trendSum=4 ≥3.
        // alignedRanges=4 ≥4, anchorRanges=2 (1d+1w) ≥2 → IsBullish passes.
        var sut = new LongTimeframeMultiTrendBullishConditionMet(minTrendCountSum: 3);
        var osci = BuildLongRangeStats(bullishRanges: ["2h", "12h", "1d", "1w"]);
        var ma = BuildLongRangeStats(bullishRanges: ["2h", "12h", "1d", "1w"]);

        Assert.That(sut.IsConditionsMet(osci, ma, null, null, 1m), Is.True);
        Assert.That(sut.TradeAdvice, Is.EqualTo(TradeAdvice.Buy));
    }

    [Test]
    public void MultiTrendBullish_WhenConfluencePassesButTrendSumBelowThreshold_ReturnsFalse()
    {
        // 4 aligned ranges (confluence passes), threshold=5, trendSum=4 < 5 → must fail.
        var sut = new LongTimeframeMultiTrendBullishConditionMet(minTrendCountSum: 5);
        var osci = BuildLongRangeStats(bullishRanges: ["2h", "12h", "1d", "1w"]);
        var ma = BuildLongRangeStats(bullishRanges: ["2h", "12h", "1d", "1w"]);

        // BuildStat gives 1 medium trend per aligned range → trendSum=4 < 5.
        Assert.That(sut.IsConditionsMet(osci, ma, null, null, 1m), Is.False);
    }

    [Test]
    public void MultiTrendBullish_WhenBaseConfluenceAlreadyFails_ReturnsFalse()
    {
        var sut = new LongTimeframeMultiTrendBullishConditionMet(minTrendCountSum: 1);
        var osci = BuildLongRangeStats(bullishRanges: ["2h", "4h"]);  // only 2 aligned – fails base gate
        var ma = BuildLongRangeStats(bullishRanges: ["2h", "4h"]);

        Assert.That(sut.IsConditionsMet(osci, ma, null, null, 1m), Is.False);
    }

    [Test]
    public void MultiTrendBearish_WhenConfluencePassesAndTrendSumMeetsThreshold_ReturnsTrueAndSellAdvice()
    {
        var sut = new LongTimeframeMultiTrendBearishConditionMet(minTrendCountSum: 3);
        var osci = BuildLongRangeStats(bearishRanges: ["4h", "12h", "1d"]);
        var ma = BuildLongRangeStats(bearishRanges: ["4h", "12h", "1d"]);

        Assert.That(sut.IsConditionsMet(osci, ma, null, null, 1m), Is.True);
        Assert.That(sut.TradeAdvice, Is.EqualTo(TradeAdvice.Sell));
    }

    [Test]
    public void MultiTrendBearish_WhenTrendSumBelowThreshold_ReturnsFalse()
    {
        var sut = new LongTimeframeMultiTrendBearishConditionMet(minTrendCountSum: 5);
        var osci = BuildLongRangeStats(bearishRanges: ["4h", "12h", "1d"]);
        var ma = BuildLongRangeStats(bearishRanges: ["4h", "12h", "1d"]);

        // trendSum=3 < 5 → fails.
        Assert.That(sut.IsConditionsMet(osci, ma, null, null, 1m), Is.False);
    }

    [Test]
    public void MultiTrendBullish_Constructor_ThrowsWhenMinTrendCountSumIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new LongTimeframeMultiTrendBullishConditionMet(minTrendCountSum: 0));
    }

    // ─── Utility ─────────────────────────────────────────────────────────────

    [Test]
    public void ComputeTrendCountSum_SumsAllLongRanges()
    {
        var osci = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            ["2h"]  = new() { IndicatorStrongTrendCount = 2, IndicatorMediumTrendCount = 1 },
            ["4h"]  = new() { IndicatorStrongTrendCount = 0, IndicatorMediumTrendCount = 3 },
            ["12h"] = new() { IndicatorStrongTrendCount = 1, IndicatorMediumTrendCount = 0 },
            ["1d"]  = new() { IndicatorStrongTrendCount = 0, IndicatorMediumTrendCount = 0 },
            ["1w"]  = new() { IndicatorStrongTrendCount = 1, IndicatorMediumTrendCount = 2 },
        };

        var sum = LongTimeframeConfluenceEvaluator.ComputeTrendCountSum(osci);

        // 3 + 3 + 1 + 0 + 3 = 10
        Assert.That(sum, Is.EqualTo(10));
    }

    // ─── Linear conditions ────────────────────────────────────────────────────

    [Test]
    public void LinearBullish_WhenScoreAboveThreshold_ReturnsTrueAndBuyAdvice()
    {
        var sut = new LongTimeframeLinearBullishConditionMet(threshold: 10.0);
        var osci = BuildLongRangeStats(bullishRanges: ["2h", "4h", "12h", "1d"]);
        var ma = BuildLongRangeStats();

        Assert.That(sut.IsConditionsMet(osci, ma, null, null, 1m), Is.True);
        Assert.That(sut.TradeAdvice, Is.EqualTo(TradeAdvice.Buy));
    }

    [Test]
    public void LinearBullish_WhenScoreBelowThreshold_ReturnsFalse()
    {
        var sut = new LongTimeframeLinearBullishConditionMet(threshold: 50.0);
        var osci = BuildLongRangeStats(bullishRanges: ["2h", "12h", "1d"]);
        var ma = BuildLongRangeStats();

        Assert.That(sut.IsConditionsMet(osci, ma, null, null, 1m), Is.False);
    }

    [Test]
    public void LinearBearish_WhenScoreAboveThreshold_ReturnsTrueAndSellAdvice()
    {
        var sut = new LongTimeframeLinearBearishConditionMet(threshold: 10.0);
        var osci = BuildLongRangeStats(bearishRanges: ["2h", "4h", "12h", "1d"]);
        var ma = BuildLongRangeStats();

        Assert.That(sut.IsConditionsMet(osci, ma, null, null, 1m), Is.True);
        Assert.That(sut.TradeAdvice, Is.EqualTo(TradeAdvice.Sell));
    }

    [Test]
    public void ComputeLinearScores_WhenBullishData_BullishScoreHigherThanBearishScore()
    {
        var osci = BuildLongRangeStats(bullishRanges: ["2h", "4h", "12h", "1d"]);

        var bullish = LongTimeframeConfluenceEvaluator.ComputeLinearBullishScore(osci);
        var bearish = LongTimeframeConfluenceEvaluator.ComputeLinearBearishScore(osci);

        Assert.That(bullish, Is.GreaterThan(bearish));
        Assert.That(bullish, Is.GreaterThan(10.0));
    }

    [Test]
    public void ComputeLinearScores_WhenBearishData_BearishScoreHigherThanBullishScore()
    {
        var osci = BuildLongRangeStats(bearishRanges: ["2h", "4h", "12h", "1d"]);

        var bullish = LongTimeframeConfluenceEvaluator.ComputeLinearBullishScore(osci);
        var bearish = LongTimeframeConfluenceEvaluator.ComputeLinearBearishScore(osci);

        Assert.That(bearish, Is.GreaterThan(bullish));
        Assert.That(bearish, Is.GreaterThan(10.0));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Dictionary<string, BitcoinTimeRangeStatistics> BuildLongRangeStats(
        string[]? bullishRanges = null,
        string[]? bearishRanges = null)
    {
        bullishRanges ??= [];
        bearishRanges ??= [];

        var allRanges = new[] { "2h", "4h", "12h", "1d", "1w" };
        var map = new Dictionary<string, BitcoinTimeRangeStatistics>();
        foreach (var range in allRanges)
        {
            if (bullishRanges.Contains(range))
            {
                map[range] = BuildStat(1, 3, true);
            }
            else if (bearishRanges.Contains(range))
            {
                map[range] = BuildStat(3, 1, true);
            }
            else
            {
                map[range] = BuildStat(1, 1, false);
            }
        }

        return map;
    }

    private static BitcoinTimeRangeStatistics BuildStat(int sell, int buy, bool mediumTrend)
    {
        return new BitcoinTimeRangeStatistics
        {
            IndicatorSellCount = sell,
            IndicatorBuyCount = buy,
            IndicatorMediumTrendCount = mediumTrend ? 1 : 0,
            IndicatorStrongTrendCount = 0,
        };
    }
}
