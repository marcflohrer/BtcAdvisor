using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Conditions;

public interface IConditionMet
{
    public bool IsConditionsMet(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? currentPrice,
        decimal? lowestBuyAfterFireSell = null,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell = null);

    public TradeAdvice TradeAdvice { get; set; }
}