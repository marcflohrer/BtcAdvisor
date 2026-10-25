using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;

// Static import for direct access to trend detection methods

namespace BtcAdvisor.Services.Conditions;

public class BuyFormulaConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.Buy;

    public bool IsConditionsMet(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? currentPrice,
        decimal? lowestBuyAfterFireSell = null,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell = null)
    {
        return BuyFormulaService.IsBuyAdvice(oscillatorStatistics, movingAverageStatistics);
    }
}
