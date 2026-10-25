using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Conditions;

public class LongTimeframeBearishConfluenceConditionMet : IConditionMet
{
    public LongTimeframeConfluenceThresholdSet ThresholdSet { get; }

    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.Sell;

    public LongTimeframeBearishConfluenceConditionMet(LongTimeframeConfluenceThresholdSet? thresholdSet = null)
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
        return LongTimeframeConfluenceEvaluator.IsBearish(
            oscillatorStatistics,
            movingAverageStatistics,
            ThresholdSet);
    }
}
