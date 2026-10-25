using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;
using static BtcAdvisor.Services.Conditions.TrendDetector;

namespace BtcAdvisor.Services.Conditions;

public class Sell500ConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.StrictSell500;

    // #buy > 0 && #sell == 0 && >=WeakTrend => knockout++
    // #buy > 0 && #sell == 0 && NoTrend ==> malus++
    // #buy = 0 && #sell > 2 && StrongTrend ==> bonus += 2 * #sell 
    // #buy = 0 && #sell > 1 && NoTrend ==> bonuy += #sell

    // 3x no buy intervalls                       (min 3)
    // 1x no buy, >=1x sell >= MediumTrend        (min 1)
    // 1x max 2x buy & >=2x sell & >= MediumTrend (min 1)
    // 2x no buy, 3x sell                         (min 2)
    // 1x max 1 buy, >= 2xsell StrongTrend        (min 1)

    // ok = knockout == 0 && bonus > malus && bonus > 3 
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
        int knockoutCount = 0;
        int malusCount = 0;
        int bonusCount = 0;
        int noBuyIntervals = 0;
        int noBuyOneSellMediumTrendIntervals = 0;
        int maxTwoBuyTwoSellMediumTrendIntervals = 0;
        int noBuyThreeSellIntervals = 0;
        int maxOneBuyTwoSellStrongTrendIntervals = 0;

        foreach (var timeRange in timeRangeForDecision)
        {
            if (oscillatorStatistics.TryGetValue(timeRange, out var osciStat))
            {
                if (osciStat.IndicatorBuyCount > 0 && osciStat.IndicatorSellCount == 0 && HasAnyTrend(osciStat))
                {
                    knockoutCount++;
                }
                if (osciStat.IndicatorBuyCount > 0 && osciStat.IndicatorSellCount == 0 && !HasAnyTrend(osciStat))
                {
                    malusCount++;
                }
                if (osciStat.IndicatorSellCount > 2 && osciStat.IndicatorBuyCount == 0 && HasStrongTrend(osciStat))
                {
                    bonusCount += (2 * osciStat.IndicatorSellCount);
                }
                if (osciStat.IndicatorSellCount > 1 && osciStat.IndicatorBuyCount == 0 && !HasAnyTrend(osciStat))
                {
                    bonusCount += osciStat.IndicatorSellCount;
                }
                if (osciStat.IndicatorBuyCount == 0)
                {
                    noBuyIntervals++;
                }
                if (osciStat.IndicatorBuyCount == 0 && osciStat.IndicatorSellCount >= 1 && HasMediumOrStrongTrend(osciStat))
                {
                    noBuyOneSellMediumTrendIntervals++;
                }
                if (osciStat.IndicatorBuyCount <= 2 && osciStat.IndicatorSellCount >= 2 && HasMediumOrStrongTrend(osciStat))
                {
                    maxTwoBuyTwoSellMediumTrendIntervals++;
                }
                if (osciStat.IndicatorBuyCount == 0 && osciStat.IndicatorSellCount >= 3)
                {
                    noBuyThreeSellIntervals++;
                }
                if (osciStat.IndicatorBuyCount <= 1 && osciStat.IndicatorSellCount >= 2 && HasStrongTrend(osciStat))
                {
                    maxOneBuyTwoSellStrongTrendIntervals++;
                }
            }
        }

        return ValidateSellConditions(knockoutCount,
                malusCount,
                bonusCount,
                noBuyIntervals,
                noBuyOneSellMediumTrendIntervals,
                maxTwoBuyTwoSellMediumTrendIntervals,
                noBuyThreeSellIntervals,
                maxOneBuyTwoSellStrongTrendIntervals);
    }

    private bool ValidateSellConditions(int knockoutCount,
                                        int malusCount,
                                        int bonusCount,
                                        int noBuyIntervals,
                                        int noBuyOneSellMediumTrendIntervals,
                                        int maxTwoBuyTwoSellMediumTrendIntervals,
                                        int noBuyThreeSellIntervals,
                                        int maxOneBuyTwoSellStrongTrendIntervals)
    {
        return knockoutCount == 0 &&
               bonusCount >= malusCount && 
               bonusCount > 3 && 
               noBuyIntervals > 2 && 
               noBuyOneSellMediumTrendIntervals > 0 && 
               maxTwoBuyTwoSellMediumTrendIntervals > 0 && 
               noBuyThreeSellIntervals > 1 && 
               maxOneBuyTwoSellStrongTrendIntervals > 0;
    }
}
