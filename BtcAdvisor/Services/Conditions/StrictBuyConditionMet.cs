using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;

using static BtcAdvisor.Services.Conditions.TrendDetector;

namespace BtcAdvisor.Services.Conditions;

public class StrictBuyConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.StrictBuy;

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
        int countBuyConditionsMet = 0;
        int countSellTrend = 0;
        int countSellIndicatorsWithoutTrend = 0;
        int bonusCount = 0;
        int malusCount = 0;
        int knockOutCount = 0;

        /*
        Knock-out: Strong Trend mit Verkaufsempfehlung
        Malus: Osz-Zeitbereich mit 0 Kauf und >0 Verkauf
        Bonus: 0 Verkauf 0 Kauf > 2 und Strong. Anzahl Kauf = Anzahl Bonus
        Malus: Moving Average-Zeitbereich mit #Verkauf - 2 > #Kauf
        */

        foreach (var timeRange in timeRangeForDecision)
        {
            if (oscillatorStatistics.TryGetValue(timeRange, out var osciStat))
            {
                if (osciStat.IndicatorSellCount > 0 && HasStrongTrend(osciStat))
                {
                    knockOutCount++;
                }
                if (osciStat.IndicatorSellCount > 0 && osciStat.IndicatorBuyCount == 0)
                {
                    malusCount++;
                }
                if (osciStat.IndicatorBuyCount > 2 && HasStrongTrend(osciStat))
                {
                    bonusCount += (2 * osciStat.IndicatorBuyCount);
                }

                if (osciStat.IndicatorSellCount == 0 && osciStat.IndicatorBuyCount > 0 && HasAnyTrend(osciStat))
                {
                    countBuyConditionsMet++;
                }
                else if (osciStat.IndicatorSellCount > 0 && HasAnyTrend(osciStat))
                {
                    countSellTrend++;
                }
                else if(osciStat.IndicatorSellCount > 2)
                {
                    countSellIndicatorsWithoutTrend++;
                }
            }
            else
            {
                Console.WriteLine($"{DateTime.UtcNow.ToString()} ERROR: [StrictBuyConditionsMet] Cannot find time range {timeRange} in bitcoin oscillator statistics.");
            }
            
            if (movingAverageStatistics.TryGetValue(timeRange, out var movingAvgStat))
            {
                if (movingAvgStat.IndicatorSellCount - 2 > movingAvgStat.IndicatorBuyCount)
                {
                    malusCount++;
                }
            }
            else
            {
                Console.WriteLine($"{DateTime.UtcNow.ToString()} ERROR: [StrictBuyConditionsMet] Cannot find time range {timeRange} in bitcoin moving average statistics.");
            }
        }
        var knockOutCondition = knockOutCount == 0;
        var result = countBuyConditionsMet >= 2 
                && countSellTrend == 0 
                && countSellIndicatorsWithoutTrend == 0 
                && knockOutCondition
                && bonusCount > malusCount;
        return result;
    }
}
