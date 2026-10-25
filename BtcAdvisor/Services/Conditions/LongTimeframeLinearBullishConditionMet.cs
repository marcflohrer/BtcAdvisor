using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Conditions;

public class LongTimeframeLinearBullishConditionMet : IConditionMet
{
    public double Threshold { get; }

    /// <summary>
    /// When set, the TC (TrendCountSum) of the current candle must be ≤ this value for the
    /// linear-bullish condition to fire. Mirrors the <c>BullishMaxTrendCountSum</c> gate that
    /// already exists on the Confluence path, preventing TC=2+ entries from falling through to
    /// this path when the Confluence cap blocks them.
    /// </summary>
    public LongTimeframeConfluenceThresholdSet ThresholdSet { get; }

    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.Buy;

    public LongTimeframeLinearBullishConditionMet(
        double threshold = 18.0,
        LongTimeframeConfluenceThresholdSet? thresholdSet = null)
    {
        Threshold = threshold;
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
        if (!LongTimeframeConfluenceEvaluator.IsBullishLinear(
                oscillatorStatistics,
                movingAverageStatistics,
                Threshold))
        {
            return false;
        }

        var maxTc = ThresholdSet.GetMaxTrendCountSum(isBullish: true);
        if (maxTc < int.MaxValue)
        {
            var tc = LongTimeframeConfluenceEvaluator.ComputeTrendCountSum(oscillatorStatistics);
            if (tc > maxTc)
            {
                return false;
            }
        }

        return true;
    }
}