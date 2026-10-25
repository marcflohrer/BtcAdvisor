using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Conditions;

/// <summary>
/// Stricter variant of <see cref="LongTimeframeBullishConfluenceConditionMet"/>.
/// In addition to the standard confluence gate (≥4 aligned ranges, ≥2 anchors aligned, 0 anchor conflicts),
/// this condition also requires the total number of strong + medium trend indicators across all long
/// timeframe ranges to reach <see cref="MinTrendCountSum"/>.
///
/// This prevents firing on days with technically aligned but trend-weak statistics, which tend to appear
/// on flat/consolidation days rather than genuine local lows.
///
/// See ADR-007 for calibrated thresholds and empirical evidence.
/// </summary>
public class LongTimeframeMultiTrendBullishConditionMet : IConditionMet
{
    /// <summary>
    /// Minimum total number of strong + medium trend indicators required across all long timeframe
    /// ranges. Default 3, configurable via constructor.
    /// </summary>
    public int MinTrendCountSum { get; }

    public LongTimeframeConfluenceThresholdSet ThresholdSet { get; }

    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.Buy;

    public LongTimeframeMultiTrendBullishConditionMet(
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
        return LongTimeframeConfluenceEvaluator.IsBullishWithMinTrend(
            oscillatorStatistics,
            movingAverageStatistics,
            MinTrendCountSum,
            ThresholdSet);
    }
}
