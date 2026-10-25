using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;
using static BtcAdvisor.Services.Conditions.TrendDetector;

namespace BtcAdvisor.Services.Conditions;

public class Buy2KConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.Buy2k;

    public bool IsConditionsMet(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? currentPrice,
        decimal? lowestBuyAfterFireSell = null,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell = null)
    {
        var timeRangeForDecision = ConditionTimeRangeProfile.ResolveDecisionRanges(oscillatorStatistics, movingAverageStatistics);
        var countNoSellIndicator = 0;
        var countMinOneBuyIndicator = 0;
        var countMinTwoBuyIndicator = 0;
        var countMinThreeBuyIndicator = 0;
        var countMinOneBuyWithStrongTrendIndicator = 0;

        foreach (var timeRange in timeRangeForDecision)
        {
            if (oscillatorStatistics.TryGetValue(timeRange, out var stat))
            {
                if (stat.IndicatorSellCount == 0)
                {
                    countNoSellIndicator++;
                }
                if (stat.IndicatorBuyCount > 0)
                {
                    countMinOneBuyIndicator++;
                }
                if (stat.IndicatorBuyCount > 1)
                {
                    countMinTwoBuyIndicator++;
                }
                if (stat.IndicatorBuyCount > 2)
                {
                    countMinThreeBuyIndicator++;
                }
                if (stat.IndicatorBuyCount > 0 && HasStrongTrend(stat) && timeRange != "2h")
                {
                    countMinOneBuyWithStrongTrendIndicator++;
                }
            }
        }
        return countNoSellIndicator == 5
                && countMinOneBuyIndicator >= 4
                && countMinTwoBuyIndicator > 0
                && (countMinThreeBuyIndicator > 1 || (countMinOneBuyIndicator > 4 && countMinTwoBuyIndicator > 2))
                && countMinOneBuyWithStrongTrendIndicator > 0;
    }
}
