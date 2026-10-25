using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Conditions;

public class LongTimeframeLinearBearishConditionMet : IConditionMet
{
    public double Threshold { get; }

    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.Sell;

    public LongTimeframeLinearBearishConditionMet(double threshold = 20.0)
    {
        Threshold = threshold;
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
        return LongTimeframeConfluenceEvaluator.IsBearishLinear(
            oscillatorStatistics,
            movingAverageStatistics,
            Threshold);
    }
}