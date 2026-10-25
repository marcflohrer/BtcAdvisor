using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;
using static BtcAdvisor.Services.Conditions.TrendDetector;

namespace BtcAdvisor.Services.Conditions;

public class HalloweenSellConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.HalloweenSell;

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
        var counts = new ConditionCounts();
        foreach (var timeRange in timeRangeForDecision)
        {
            if (oscillatorStatistics.TryGetValue(timeRange, out var osciStat))
            {
                UpdateCountsFromOscillatorStats(osciStat, counts, timeRange, firstRange);
            }
            if (movingAverageStatistics.TryGetValue(timeRange, out var movAvgStats))
            {
                UpdateCountsFromMovingAverageStats(movAvgStats, counts);
            }
        }

        return ValidateConditions(counts);
    }

    private void UpdateCountsFromOscillatorStats(BitcoinTimeRangeStatistics osciStat, ConditionCounts counts, string timeRange, string firstRange)
    {
        counts.NoOsciBuyTimeRangeCount += osciStat.IndicatorBuyCount == 0 ? 1 : 0;
        counts.MoreOsciBuyThanSellTimeRangeCount += osciStat.IndicatorBuyCount > osciStat.IndicatorSellCount ? 1 : 0;
        if (osciStat.IndicatorBuyCount == osciStat.IndicatorSellCount
            && osciStat.IndicatorBuyCount < 2
            && timeRange == firstRange)
        {
            counts.BuyEqualsSellOsciTimeRangeCount++;
        }
        counts.MinTwoOsciSellAndNoBuyTimeRangeCount += (osciStat.IndicatorSellCount > 1 && osciStat.IndicatorBuyCount == 0) ? 1 : 0;
        counts.MinOneOsciSellAndNoBuyTimeRangeCount += (osciStat.IndicatorSellCount > 0 && osciStat.IndicatorBuyCount == 0) ? 1 : 0;
        counts.NoBuyMinOneSellWithMinMediumTrendTimeRangeCount += (osciStat.IndicatorSellCount > 0 && osciStat.IndicatorBuyCount == 0 && HasMediumOrStrongTrend(osciStat)) ? 1 : 0;
    }

    private void UpdateCountsFromMovingAverageStats(BitcoinTimeRangeStatistics movAvgStats, ConditionCounts counts)
    {
        counts.MinSevenMovAvgSellTimeRangeCount += (movAvgStats.IndicatorBuyCount == 0 && movAvgStats.IndicatorSellCount >= 7) ? 1 : 0;
        counts.MinFiveMovAvgSellAndMaxOneBuyTimeRangeCount += (movAvgStats.IndicatorBuyCount < 2 && movAvgStats.IndicatorSellCount > 4) ? 1 : 0;
        counts.MinFourMovAvgSellAndMaxTwoBuyTimeRangeCount += (movAvgStats.IndicatorBuyCount < 3 && movAvgStats.IndicatorSellCount > 3) ? 1 : 0;
        counts.MinTwoMovAvgSellAndMaxFourBuyTimeRangeCount += (movAvgStats.IndicatorBuyCount < 5 && movAvgStats.IndicatorSellCount > 1) ? 1 : 0;
    }

    private bool ValidateConditions(ConditionCounts counts)
    {
        return counts.NoOsciBuyTimeRangeCount > 3 &&
            counts.MoreOsciBuyThanSellTimeRangeCount == 0 &&
            counts.BuyEqualsSellOsciTimeRangeCount < 2 &&
            counts.MinTwoOsciSellAndNoBuyTimeRangeCount > 1 &&
            counts.MinOneOsciSellAndNoBuyTimeRangeCount > 2 &&
            counts.NoBuyMinOneSellWithMinMediumTrendTimeRangeCount > 0 &&
            counts.MinSevenMovAvgSellTimeRangeCount > 0 &&
            counts.MinFiveMovAvgSellAndMaxOneBuyTimeRangeCount > 1 &&
            counts.MinFourMovAvgSellAndMaxTwoBuyTimeRangeCount > 3 &&
            counts.MinTwoMovAvgSellAndMaxFourBuyTimeRangeCount > 4;
    }

    private class ConditionCounts
    {
        public int NoOsciBuyTimeRangeCount { get; set; }
        public int MoreOsciBuyThanSellTimeRangeCount { get; set; }
        public int BuyEqualsSellOsciTimeRangeCount { get; set; }
        public int MinTwoOsciSellAndNoBuyTimeRangeCount { get; set; }
        public int MinOneOsciSellAndNoBuyTimeRangeCount { get; set; }
        public int NoBuyMinOneSellWithMinMediumTrendTimeRangeCount { get; set; }
        public int MinSevenMovAvgSellTimeRangeCount { get; set; }
        public int MinFiveMovAvgSellAndMaxOneBuyTimeRangeCount { get; set; }
        public int MinFourMovAvgSellAndMaxTwoBuyTimeRangeCount { get; set; }
        public int MinTwoMovAvgSellAndMaxFourBuyTimeRangeCount { get; set; }
    }
}
