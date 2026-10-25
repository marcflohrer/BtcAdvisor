using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Conditions;

public class LongTimeframeBullishConfluenceConditionMet : IConditionMet
{
    public LongTimeframeConfluenceThresholdSet ThresholdSet { get; }

    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.Buy;

    public LongTimeframeBullishConfluenceConditionMet(LongTimeframeConfluenceThresholdSet? thresholdSet = null)
    {
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
        return LongTimeframeConfluenceEvaluator.IsBullish(
            oscillatorStatistics,
            movingAverageStatistics,
            ThresholdSet);
    }
}
