using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;
using static BtcAdvisor.Services.Conditions.TrendDetector;

namespace BtcAdvisor.Services.Conditions;

public class Sell1kConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.StrictSell1k;

    //no buy & >=3 sell & StrongTrend:       (min 1)
    //max 1 buy & >= 3 sell & StrongTrend:   (min 1)
    //max 1 buy & >= 1 sell & StrongTrend:   (min 2)
    //more sell than buy:                    (min 3)
    //max 1 buy & >= 1 sell & >= MediumTrend:(min 3)
    //#buy indicators:                       (max 5)

    //#intervals with min 1 sell indicator = 5
    //#intervals with min 2 sell indicator = 3
    //#intervals with min 1 sell indicator & strong trend >= 2
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
        int countSellIndicator = 0;
        int countMinTwoSellIndicator = 0;
        int countMinOneSellWithStrongTrendIndicator = 0;

        int noBuyAndMin3SellAndStrongTrend = 0;
        int max1BuyAndMin3SellAndStrongTrend = 0;
        int max1BuyAndMin1SellAndStrongTrend = 0;
        int moreSellThanBuy = 0;
        int max1BuyAndMin1SellAndMinMediumTrend = 0;
        int numberOfBuyIndicators = 0;

        foreach (var timeRange in timeRangeForDecision)
        {
            if (oscillatorStatistics.TryGetValue(timeRange, out var stat))
            {
                if (stat.IndicatorSellCount > 0)
                {
                    countSellIndicator++;
                    if (stat.IndicatorSellCount > 1)
                    {
                        countMinTwoSellIndicator++;
                    }
                    if (HasStrongTrend(stat))
                    {
                        countMinOneSellWithStrongTrendIndicator++;
                    }
                }
                if(stat.IndicatorBuyCount == 0 && stat.IndicatorSellCount > 2 && HasStrongTrend(stat))
                {
                    noBuyAndMin3SellAndStrongTrend++;
                }
                if(stat.IndicatorBuyCount < 2 && stat.IndicatorSellCount > 2 && HasStrongTrend(stat))
                {
                    max1BuyAndMin3SellAndStrongTrend++;
                }
                if(stat.IndicatorBuyCount < 2 && stat.IndicatorSellCount > 0 && HasStrongTrend(stat))
                {
                    max1BuyAndMin1SellAndStrongTrend++;
                }
                if(stat.IndicatorBuyCount < stat.IndicatorSellCount)
                {
                    moreSellThanBuy++;
                }
                if(stat.IndicatorBuyCount < 2 && stat.IndicatorSellCount > 0 && HasMediumOrStrongTrend(stat))
                {
                    max1BuyAndMin1SellAndMinMediumTrend++;
                }
                if(stat.IndicatorBuyCount > 0)
                {
                    numberOfBuyIndicators += stat.IndicatorBuyCount;
                }
            }
        }

        return ValidateSellConditions(countSellIndicator,
                                        countMinTwoSellIndicator,
                                        countMinOneSellWithStrongTrendIndicator,
                                        noBuyAndMin3SellAndStrongTrend,
                                        max1BuyAndMin3SellAndStrongTrend,
                                        max1BuyAndMin1SellAndStrongTrend,
                                        moreSellThanBuy,
                                        max1BuyAndMin1SellAndMinMediumTrend,
                                        numberOfBuyIndicators);
    }

    private bool ValidateSellConditions(int countSellIndicator, 
                                        int countMinTwoSellIndicator, 
                                        int countMinOneSellWithStrongTrendIndicator,
                                        int noBuyAndMin3SellAndStrongTrend,
                                        int max1BuyAndMin3SellAndStrongTrend,
                                        int max1BuyAndMin1SellAndStrongTrend,
                                        int moreSellThanBuy,
                                        int max1BuyAndMin1SellAndMinMediumTrend,
                                        int numberOfBuyIndicators)
    {
        return countSellIndicator == 5 &&
               countMinTwoSellIndicator >= 3 &&
               countMinOneSellWithStrongTrendIndicator >= 2 &&
               noBuyAndMin3SellAndStrongTrend >= 1 && 
               max1BuyAndMin3SellAndStrongTrend >= 1 &&
               max1BuyAndMin1SellAndStrongTrend >= 2 &&
               moreSellThanBuy >= 3 &&
               max1BuyAndMin1SellAndMinMediumTrend >= 3 &&
               numberOfBuyIndicators <= 5;
    }
}
