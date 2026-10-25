using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Conditions;

/// <summary>
/// Stricter variant of <see cref="LongTimeframeBearishConfluenceConditionMet"/>.
/// In addition to the standard confluence gate (≥3 aligned ranges, ≥0 anchors aligned, 0 anchor conflicts,
/// trendCountSum ≥2), this condition also requires the total trend indicator count to reach
/// <see cref="MinTrendCountSum"/> (default 3).
///
/// See ADR-007 for calibrated thresholds and empirical evidence.
/// </summary>
public class LongTimeframeMultiTrendBearishConditionMet : IConditionMet
{
    /// <summary>
    /// Minimum total number of strong + medium trend indicators required across all long timeframe
    /// ranges. Default 3, configurable via constructor.
    /// </summary>
    public int MinTrendCountSum { get; }

    public LongTimeframeConfluenceThresholdSet ThresholdSet { get; }

    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.Sell;

    public LongTimeframeMultiTrendBearishConditionMet(
        int minTrendCountSum = 3,
        LongTimeframeConfluenceThresholdSet? thresholdSet = null)
    {
        if (minTrendCountSum < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minTrendCountSum), "Must be at least 1.");
        }

        MinTrendCountSum = minTrendCountSum;
        ThresholdSet = thresholdSet ?? LongTimeframeConfluenceThresholdSet.Default;
    }

    public bool IsConditionsMet(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? currentPrice,
        decimal? lowestBuyAfterFireSell = null,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell = null)
    {
        return LongTimeframeConfluenceEvaluator.IsBearishWithMinTrend(
            oscillatorStatistics,
            movingAverageStatistics,
            MinTrendCountSum,
            ThresholdSet);
    }
}
