using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions;

namespace BtcAdvisor.Tests.Services;

/// <summary>
/// Regression tests for SellConditionsMet (StrictSell).
/// Bug: min1BuyNoSellAnyTrend was incremented for buy-only intervals WITHOUT trend,
///      even though the counter is documented as "Min 1 buy &amp; no sell &amp; Any Trend".
///      A trendless buy-only interval is NOT a counter-signal and must not act as a knockout.
/// Fix: Added &amp;&amp; HasAnyTrend(stat) to the condition.
/// Note: Due to the mathematical constraint noBuyInterval >= 4 with 5 timeframes,
///       min1BuyNoSellAnyTrend can be at most 1 — so the bug cannot flip the result in
///       a 5-timeframe setup. This test documents the correct semantic behavior.
/// </summary>
[TestFixture]
public class SellConditionsMetTests
{
    private SellConditionMet _sut = null!;

    [SetUp]
    public void Setup()
    {
        _sut = new SellConditionMet();
    }

    /// <summary>
    /// A valid sell scenario where the single buy-only timeframe has NO trend.
    /// This interval must NOT count as a knockout (min1BuyNoSellAnyTrend must stay 0).
    /// Result: condition met.
    /// </summary>
    [Test]
    public void IsConditionsMet_WhenBuyOnlyIntervalHasNoTrend_ShouldReturnTrue()
    {
        // 4 timeframes: buy=0, sell=2, medium trend → all four sell conditions satisfied
        // 1 timeframe (2h): buy=1, sell=0, NO trend → after fix: NOT a knockout
        var oscillatorStatistics = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            { "5m",  BuildStats(sellCount: 2, buyCount: 0, isMediumTrend: true) },
            { "15m", BuildStats(sellCount: 2, buyCount: 0, isMediumTrend: true) },
            { "30m", BuildStats(sellCount: 2, buyCount: 0, isMediumTrend: true) },
            { "1h",  BuildStats(sellCount: 2, buyCount: 0, isMediumTrend: true) },
            { "2h",  BuildStats(sellCount: 0, buyCount: 1, isMediumTrend: false) }, // no trend
        };

        var result = _sut.IsConditionsMet(oscillatorStatistics,
            movingAverageStatistics: new Dictionary<string, BitcoinTimeRangeStatistics>(),
            latestBuyPrice: null, latestSellPrice: null, currentPrice: null);

        Assert.That(result, Is.True,
            "A trendless buy-only interval must not count toward the min1BuyNoSellAnyTrend knockout.");
    }

    /// <summary>
    /// A buy-only interval WITH trend must count as a counter-signal (knockout when > 1).
    /// With 1 such interval the condition is still met (limit is <= 1, so exactly 1 passes).
    /// </summary>
    [Test]
    public void IsConditionsMet_WhenBuyOnlyIntervalHasWeakTrend_ShouldReturnTrue()
    {
        var oscillatorStatistics = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            { "5m",  BuildStats(sellCount: 2, buyCount: 0, isMediumTrend: true) },
            { "15m", BuildStats(sellCount: 2, buyCount: 0, isMediumTrend: true) },
            { "30m", BuildStats(sellCount: 2, buyCount: 0, isMediumTrend: true) },
            { "1h",  BuildStats(sellCount: 2, buyCount: 0, isMediumTrend: true) },
            { "2h",  BuildStats(sellCount: 0, buyCount: 1, isWeakTrend: true) }, // weak trend counts
        };

        var result = _sut.IsConditionsMet(oscillatorStatistics,
            movingAverageStatistics: new Dictionary<string, BitcoinTimeRangeStatistics>(),
            latestBuyPrice: null, latestSellPrice: null, currentPrice: null);

        Assert.That(result, Is.True,
            "A single buy-only interval with weak trend (min1BuyNoSellAnyTrend = 1) still satisfies the <= 1 limit.");
    }

    /// <summary>
    /// Without enough sell timeframes, the condition must fail regardless.
    /// </summary>
    [Test]
    public void IsConditionsMet_WhenInsufficientSellSignals_ShouldReturnFalse()
    {
        // Only 1 sell timeframe → countSellConditionsMet = 1 < 2 required
        var oscillatorStatistics = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            { "5m",  BuildStats(sellCount: 2, buyCount: 0, isMediumTrend: true) },
            { "15m", BuildStats(sellCount: 0, buyCount: 0, isMediumTrend: false) },
            { "30m", BuildStats(sellCount: 0, buyCount: 0, isMediumTrend: false) },
            { "1h",  BuildStats(sellCount: 0, buyCount: 0, isMediumTrend: false) },
            { "2h",  BuildStats(sellCount: 0, buyCount: 0, isMediumTrend: false) },
        };

        var result = _sut.IsConditionsMet(oscillatorStatistics,
            movingAverageStatistics: new Dictionary<string, BitcoinTimeRangeStatistics>(),
            latestBuyPrice: null, latestSellPrice: null, currentPrice: null);

        Assert.That(result, Is.False, "Insufficient sell signals must not trigger StrictSell.");
    }

    private static BitcoinTimeRangeStatistics BuildStats(
        int sellCount, int buyCount,
        bool isMediumTrend = false, bool isWeakTrend = false)
        => new BitcoinTimeRangeStatistics
        {
            IndicatorSellCount = sellCount,
            IndicatorBuyCount = buyCount,
            IndicatorMediumTrendCount = isMediumTrend ? 1 : 0,
            IndicatorWeakTrendCount = isWeakTrend ? 1 : 0,
        };
}
