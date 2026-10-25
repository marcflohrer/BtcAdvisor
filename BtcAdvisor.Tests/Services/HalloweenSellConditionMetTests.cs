using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions;

namespace BtcAdvisor.Tests.Services;

/// <summary>
/// Regression tests for HalloweenSellConditionMet.
/// Bug: MinSevenMovAvgSellTimeRangeCount used == 7 (exact equality) instead of >= 7.
/// With == 7, any MA timeframe with sell count > 7 (stronger signal!) would NOT increment
/// the counter, silently failing the condition in extreme bearish markets.
/// Fix: Changed == 7 to >= 7.
/// </summary>
[TestFixture]
public class HalloweenSellConditionMetTests
{
    private HalloweenSellConditionMet _sut = null!;

    [SetUp]
    public void Setup()
    {
        _sut = new HalloweenSellConditionMet();
    }

    /// <summary>
    /// Regression: when all MA timeframes have sell=8 (> 7), the condition must still be met.
    /// Before fix (== 7): sell=8 would NOT increment MinSevenMovAvgSellTimeRangeCount → condition fails.
    /// After fix (>= 7): sell=8 DOES increment the counter → condition passes.
    /// </summary>
    [Test]
    public void IsConditionsMet_WhenMovingAverageSellCountIsEight_ShouldReturnTrue()
    {
        var oscillatorStatistics = BuildStrongSellOscillatorStats();
        var movingAverageStatistics = BuildMovingAverageStats(sellCount: 8);

        var result = _sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics,
            latestBuyPrice: null, latestSellPrice: null, currentPrice: null);

        Assert.That(result, Is.True,
            "HalloweenSell must fire when MA sell count is 8 (>= 7). Before fix, == 7 blocked this.");
    }

    /// <summary>
    /// Sanity check: sell=7 must also pass (both == 7 and >= 7 match).
    /// </summary>
    [Test]
    public void IsConditionsMet_WhenMovingAverageSellCountIsSeven_ShouldReturnTrue()
    {
        var oscillatorStatistics = BuildStrongSellOscillatorStats();
        var movingAverageStatistics = BuildMovingAverageStats(sellCount: 7);

        var result = _sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics,
            latestBuyPrice: null, latestSellPrice: null, currentPrice: null);

        Assert.That(result, Is.True, "HalloweenSell must fire when MA sell count is exactly 7.");
    }

    /// <summary>
    /// Regression: with sell=6 (less than 7), MinSevenMovAvgSellTimeRangeCount stays 0 → condition must fail.
    /// </summary>
    [Test]
    public void IsConditionsMet_WhenMovingAverageSellCountIsSix_ShouldReturnFalse()
    {
        var oscillatorStatistics = BuildStrongSellOscillatorStats();
        var movingAverageStatistics = BuildMovingAverageStats(sellCount: 6);

        var result = _sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics,
            latestBuyPrice: null, latestSellPrice: null, currentPrice: null);

        Assert.That(result, Is.False, "HalloweenSell must not fire when MA sell count is below 7.");
    }

    /// <summary>
    /// Builds oscillator stats that satisfy all HalloweenSell oscillator conditions.
    /// Pattern: 5m sell=4, 15m/30m sell=3, 1h/2h sell=2 — all buy=0, strong trend.
    /// </summary>
    private static Dictionary<string, BitcoinTimeRangeStatistics> BuildStrongSellOscillatorStats()
        => new()
        {
            { "5m",  BuildOsciStat(sellCount: 4, buyCount: 0, isStrongTrend: true) },
            { "15m", BuildOsciStat(sellCount: 3, buyCount: 0, isStrongTrend: true) },
            { "30m", BuildOsciStat(sellCount: 3, buyCount: 0, isStrongTrend: true) },
            { "1h",  BuildOsciStat(sellCount: 2, buyCount: 0, isStrongTrend: true) },
            { "2h",  BuildOsciStat(sellCount: 2, buyCount: 0, isStrongTrend: true) },
        };

    private static Dictionary<string, BitcoinTimeRangeStatistics> BuildMovingAverageStats(int sellCount)
        => new()
        {
            { "5m",  BuildMovAvgStat(sellCount: sellCount, buyCount: 0) },
            { "15m", BuildMovAvgStat(sellCount: sellCount, buyCount: 0) },
            { "30m", BuildMovAvgStat(sellCount: sellCount, buyCount: 0) },
            { "1h",  BuildMovAvgStat(sellCount: sellCount, buyCount: 0) },
            { "2h",  BuildMovAvgStat(sellCount: sellCount, buyCount: 0) },
        };

    private static BitcoinTimeRangeStatistics BuildOsciStat(int sellCount, int buyCount, bool isStrongTrend = false)
        => new BitcoinTimeRangeStatistics
        {
            IndicatorSellCount = sellCount,
            IndicatorBuyCount = buyCount,
            IndicatorStrongTrendCount = isStrongTrend ? 1 : 0,
        };

    private static BitcoinTimeRangeStatistics BuildMovAvgStat(int sellCount, int buyCount)
        => new BitcoinTimeRangeStatistics
        {
            IndicatorSellCount = sellCount,
            IndicatorBuyCount = buyCount,
        };
}
