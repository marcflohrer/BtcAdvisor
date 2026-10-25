using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;

// Static import for direct access to trend detection methods

namespace BtcAdvisor.Services.Conditions;

public class SellFormulaConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.Sell;

    public bool IsConditionsMet(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? currentPrice,
        decimal? lowestBuyAfterFireSell = null,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell = null)
    {
        return SellFormulaService.IsSellAdvice(oscillatorStatistics, movingAverageStatistics);
    }
}
