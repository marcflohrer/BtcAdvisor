using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions.Models;

namespace BtcAdvisor.Services.Conditions;

public class WitchSabbathBuyCondition : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.WitchSabbathBuy;

    public bool IsConditionsMet(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? currentPrice,
        decimal? lowestBuyAfterFireSell = null,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell = null)
    {
        var timeRanges = ConditionTimeRangeProfile.ResolveDecisionRanges(oscillatorStatistics, movingAverageStatistics);
        var osciConditions = new WitchSabbathOscillatorConditions();
        var movingAvgConditions = new WitchSabbathMovingAverageConditions();

        foreach (var timeRange in timeRanges)
        {
            if (oscillatorStatistics.TryGetValue(timeRange, out var osciStat))
            {
                UpdateOscillatorConditions(osciConditions, osciStat);
            }
            if (movingAverageStatistics.TryGetValue(timeRange, out var movingAvgStat))
            {
                UpdateMovingAverageConditions(movingAvgConditions, movingAvgStat);
            }
        }

        return osciConditions.MeetsAllConditions() && movingAvgConditions.MeetsAllConditions();
    }

    private static void UpdateOscillatorConditions(WitchSabbathOscillatorConditions conditions, BitcoinTimeRangeStatistics stats)
    {
        if (stats.IndicatorSellCount == 0)
        {
            conditions.NoSellIndicatorCount++;
        }
        if (stats.IndicatorSellCount <= 1)
        {
            conditions.MaxOneSellIndicatorCount++;
        }
        if (stats.IndicatorBuyCount > stats.IndicatorSellCount)
        {
            conditions.MoreBuyThanSellCount++;
        }
        if (stats.IndicatorBuyCount == stats.IndicatorSellCount)
        {
            conditions.BuyEqualsSellCount++;
        }
    }

    private static void UpdateMovingAverageConditions(WitchSabbathMovingAverageConditions conditions, BitcoinTimeRangeStatistics stats)
    {
        if (stats.IndicatorSellCount == 0)
        {
            conditions.NoSellIndicatorCount++;
            if (stats.IndicatorBuyCount >= 7)
            {
                conditions.MinSevenBuyAndNoSellCount++;
            }
        }
        if (stats.IndicatorBuyCount >= 5)
        {
            conditions.MinFiveBuyIndicatorCount++;
        }
        if (stats.IndicatorBuyCount >= 6)
        {
            conditions.MinSixBuyIndicatorCount++;
        }
    }
}
