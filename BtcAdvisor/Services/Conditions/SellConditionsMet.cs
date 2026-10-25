using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;

// Static import for direct access to trend detection methods
using static BtcAdvisor.Services.Conditions.TrendDetector;

namespace BtcAdvisor.Services.Conditions;

public class SellConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.StrictSell;

    // No buy interval: 4                                  (min 4)
    // No buy & 2 sell & >= Medium Trend: 1                (min 1)
    // Min 1 buy & no sell & Any Trend: 1                  (max 1)
    // No sell interval: 1                                 (max 2)
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
        
        int countSellConditionsMet = 0;
        int noBuyInterval = 0;
        int noBuy2SellMinMediumTrend = 0;
        int min1BuyNoSellAnyTrend = 0;
        int noSellInterval = 0;
        
        foreach (var timeRange in timeRangeForDecision)
        {
            if (oscillatorStatistics.TryGetValue(timeRange, out var stat))
            {
                if (stat.IndicatorBuyCount == 0 && stat.IndicatorSellCount > 0 && HasAnyTrend(stat))
                {
                    countSellConditionsMet++;
                }
                if (stat.IndicatorBuyCount == 0)
                {
                    noBuyInterval++;
                }
                if (stat.IndicatorBuyCount == 0 && stat.IndicatorSellCount >= 2 && HasMediumOrStrongTrend(stat))
                {
                    noBuy2SellMinMediumTrend++;
                }
                if (stat.IndicatorBuyCount >= 1 && stat.IndicatorSellCount == 0 && HasAnyTrend(stat))
                {
                    min1BuyNoSellAnyTrend++;
                }
                if (stat.IndicatorSellCount == 0)
                {
                    noSellInterval++;
                }
            }
            else
            {
                Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} ERROR: [SellConditionsMet] Cannot find time range {timeRange} in bitcoin statistics.");
            }
        }

        return ValidateSellConditions(countSellConditionsMet,
                                        noBuyInterval,
                                        noBuy2SellMinMediumTrend,
                                        min1BuyNoSellAnyTrend,
                                        noSellInterval);
    }

    private bool ValidateSellConditions(int countSellConditionsMet,
                                        int noBuyInterval,
                                        int noBuy2SellMinMediumTrend,
                                        int min1BuyNoSellAnyTrend,
                                        int noSellInterval) => countSellConditionsMet >= 2
                && noBuyInterval >= 4
                && noBuy2SellMinMediumTrend >= 1
                && min1BuyNoSellAnyTrend <= 1
                && noSellInterval <= 2;
}
