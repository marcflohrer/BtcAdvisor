using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;

// Import TrendDetector methods directly

namespace BtcAdvisor.Services.Conditions;

public class HiddenBuyConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.HiddenBuy;

    public bool IsConditionsMet(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        decimal? latestBuyPrice,
        decimal? latestSellAction,
        decimal? currentPrice,
        decimal? lowestBuyAfterFireSell = null,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell = null)
    {
        var timeRangeForDecision = ConditionTimeRangeProfile.ResolveDecisionRanges(oscillatorStatistics, movingAverageStatistics);
        int countNoOsciSellIndicator = 0;
        int countMinTwoOsciBuy = 0;
        int countMinThreeOsciBuy = 0;
        int countNoMovAvgSell = 0;
        int countMinSevenMovAvgBuy = 0; 
        int countMinSixMovAvgBuy = 0;

        foreach (var timeRange in timeRangeForDecision)
        {
            if (oscillatorStatistics.TryGetValue(timeRange, out var osciStat))
            {
                if (osciStat.IndicatorSellCount == 0)
                {
                    countNoOsciSellIndicator++;
                    if (osciStat.IndicatorBuyCount >= 2)
                    {
                        countMinTwoOsciBuy++;
                    }
                    if (osciStat.IndicatorBuyCount >= 3)
                    {
                        countMinThreeOsciBuy++;
                    }
                }
            }
            if (movingAverageStatistics.TryGetValue(timeRange, out var movingAvgStat))
            {
                if (movingAvgStat.IndicatorSellCount == 0)
                {
                    countNoMovAvgSell++;
                    if (movingAvgStat.IndicatorBuyCount >= 7)
                    {
                        countMinSevenMovAvgBuy++;
                    }
                    if (movingAvgStat.IndicatorBuyCount >= 6)
                    {
                        countMinSixMovAvgBuy++;
                    }
                }
            }
        }

        return countNoOsciSellIndicator >= 3 && countMinTwoOsciBuy >= 3 &&
               countMinThreeOsciBuy >= 1 && countNoMovAvgSell == 5 &&
               countMinSevenMovAvgBuy >= 2 && countMinSixMovAvgBuy == 5;
    }
}
