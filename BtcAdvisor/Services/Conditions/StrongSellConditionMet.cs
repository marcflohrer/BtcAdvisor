using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;
using static BtcAdvisor.Services.Conditions.TrendDetector;

namespace BtcAdvisor.Services.Conditions;

public class StrongSellConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.StrongSell;

    //[ 5m: //Kauf: /stoch/cci ]
    //[ 15m: //Kauf: /cci// Verkauf: /mom// Trend: adx::StrongTrend ]
    //[ 30m: // Verkauf: /macd// Trend: adx::StrongTrend ]
    //[ 1h: // Verkauf: /stochf/willr// Trend: adx::StrongTrend ]
    //[ 2h: // Verkauf: /cci/stochf/willr// Trend: adx::StrongTrend ]

    // min #4  max 1 buy & min 1 sell & StrongTrend
    // min #3: zero buy & min 1 sell & StrongTrend
    // min #2: zero buy & min 2 sell & StrongTrend
    // min #1: zero buy & min 2 sell & StrongTrend
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
        var firstRange = timeRangeForDecision[0];
        int noBuyMinOneSellStrongTrendCounter = 0;
        int noBuyMinTwoSellStrongTrendCounter = 0;
        int noBuyMinThreeSellStrongTrendCounter = 0;
        int maxOneBuyMinOneSellStrongTrendCounter = 0;

        foreach (var timeRange in timeRangeForDecision)
        {
            if (oscillatorStatistics.TryGetValue(timeRange, out var stat))
            {
                if(stat.IndicatorBuyCount == 0 && stat.IndicatorSellCount > 0 && HasStrongTrend(stat))
                {
                    noBuyMinOneSellStrongTrendCounter++;
                }
                if(stat.IndicatorBuyCount == 0 && stat.IndicatorSellCount > 1 && HasStrongTrend(stat))
                {
                    noBuyMinTwoSellStrongTrendCounter++;
                }
                if(stat.IndicatorBuyCount == 0
                    && stat.IndicatorSellCount > 2
                    && HasStrongTrend(stat)
                    && timeRange != firstRange)
                {
                    noBuyMinThreeSellStrongTrendCounter++;
                }
                if(stat.IndicatorBuyCount < 2 && stat.IndicatorSellCount > 0 && HasStrongTrend(stat))
                {
                    maxOneBuyMinOneSellStrongTrendCounter++;
                }
            }
        }

        var validationResult = ValidateSellConditions(noBuyMinOneSellStrongTrendCounter,
                                        noBuyMinTwoSellStrongTrendCounter,
                                        noBuyMinThreeSellStrongTrendCounter,
                                        maxOneBuyMinOneSellStrongTrendCounter);
        return validationResult;
    }

    private bool ValidateSellConditions(int noBuyMinOneSellStrongTrendCounter, 
                                        int noBuyMinTwoSellStrongTrendCounter, 
                                        int noBuyMinThreeSellStrongTrendCounter,
                                        int maxOneBuyMinOneSellStrongTrendCounter)
    {
        return maxOneBuyMinOneSellStrongTrendCounter > 3 &&
               noBuyMinOneSellStrongTrendCounter > 2 &&
               noBuyMinTwoSellStrongTrendCounter > 1 &&
               noBuyMinThreeSellStrongTrendCounter > 0;
    }
}
