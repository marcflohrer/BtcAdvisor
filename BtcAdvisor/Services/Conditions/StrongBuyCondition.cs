using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;
using static BtcAdvisor.Services.Conditions.TrendDetector;

namespace BtcAdvisor.Services.Conditions;

public class StrongBuyConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.StrongBuy;

    /*
     * #osci-sell = 0 && #buy > 0 && StrongTrend:: 2 * #Buy == #Bonus :>: 0 Bonuspunkte
     * #ma-sell > ma-buy ?? #Sell - #Buy == #Malus = 0
     * #Bonus > #Malus ? 0 < 0 && Bonus > 0 && FAIL
    */
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
        var secondRange = timeRangeForDecision[1];
        var thirdRange = timeRangeForDecision[2];
        var fourthRange = timeRangeForDecision[3];
        var counts = new ConditionCounts();
        foreach (var timeRange in timeRangeForDecision)
        {
            if (oscillatorStatistics.TryGetValue(timeRange, out var osciStat))
            {
                UpdateCountsFromOscillatorStats(osciStat, counts, timeRange, secondRange, thirdRange, fourthRange);
            }
            if (movingAverageStatistics.TryGetValue(timeRange, out var movAvgStats))
            {
                UpdateCountsFromMovingAverageStats(movAvgStats, counts);
            }
        }

        return ValidateConditions(counts);
    }

    private void UpdateCountsFromOscillatorStats(
        BitcoinTimeRangeStatistics osciStat,
        ConditionCounts counts,
        string timeRange,
        string secondRange,
        string thirdRange,
        string fourthRange)
    {
        counts.NoSellAndStrongTrendTimeRangeCount += osciStat.IndicatorSellCount == 0
            && HasAnyTrend(osciStat)
            && timeRange == secondRange ? 1 : 0;
        counts.NoSellAndStrongTrendTimeRangeCount += osciStat.IndicatorSellCount == 0
            && HasMediumOrStrongTrend(osciStat)
            && timeRange == thirdRange ? 1 : 0;
        counts.NoSellAndStrongTrendTimeRangeCount += osciStat.IndicatorSellCount == 0
            && HasStrongTrend(osciStat)
            && timeRange == fourthRange ? 1 : 0;
        counts.BonusCount += osciStat.IndicatorSellCount == 0 && osciStat.IndicatorBuyCount > 0 && HasStrongTrend(osciStat) ? 4 * osciStat.IndicatorBuyCount : 0;
        counts.BonusCount += osciStat.IndicatorSellCount == 0 && osciStat.IndicatorBuyCount > 0 && HasMediumTrend(osciStat) ? 2 * osciStat.IndicatorBuyCount : 0;
    }

    private void UpdateCountsFromMovingAverageStats(BitcoinTimeRangeStatistics movAvgStats, ConditionCounts counts)
    {
        counts.MalusCount += (movAvgStats.IndicatorSellCount > movAvgStats.IndicatorBuyCount) ? movAvgStats.IndicatorSellCount - movAvgStats.IndicatorBuyCount : 0;
    }

    private bool ValidateConditions(ConditionCounts counts)
    {
        return counts.NoSellAndStrongTrendTimeRangeCount > 2 &&
            counts.BonusCount > 0 &&
            counts.BonusCount > counts.MalusCount;
    }

    private class ConditionCounts
    {
        public int NoSellAndStrongTrendTimeRangeCount { get; set; }
        public int BonusCount { get; set; }
        public int MalusCount { get; set; }
    }
}
