using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Conditions;

public class FireSellConditionMet : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.FireSellStrong;

    public bool IsConditionsMet(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? currentPrice,
        decimal? lowestBuyAfterFireSell = null,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell = null)
    {
        var isStrictSellConditionMet = new SellConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var isSellFormulaConditionMet = new SellFormulaConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var isStrongSellConditionMet = new StrongSellConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var isHalloweenSellConditionMet = new HalloweenSellConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var isSell1kConditionMet = new Sell1kConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var isSell500ConditionMet = new Sell500ConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var isSellConditionMet = isStrongSellConditionMet 
            || isSellFormulaConditionMet 
            || isStrictSellConditionMet
            || isHalloweenSellConditionMet
            || isSell1kConditionMet
            || isSell500ConditionMet;
        
        if(isStrongSellConditionMet)
        {
            TradeAdvice = TradeAdvice.FireSellStrong;
        }
        else if (isSellFormulaConditionMet)
        {
            TradeAdvice = TradeAdvice.FireSellFormula;
        }
        else if (isStrictSellConditionMet)
        {
            TradeAdvice = TradeAdvice.FireSellStrict;
        }
        else if (isHalloweenSellConditionMet)
        {
            TradeAdvice = TradeAdvice.FireSellHalloween;
        }
        else if (isSell1kConditionMet)
        {
            TradeAdvice = TradeAdvice.FireSell1k;
        }
        else if (isSell500ConditionMet)
        {
            TradeAdvice = TradeAdvice.FireSell500;
        }

        var profit = currentPrice - latestBuyPrice;
        var percentage = PercentageService.GetPercentage(Convert.ToDouble(profit), Convert.ToDouble(latestBuyPrice));
        var isProfitGreaterThanPi = percentage > double.Pi;
        return isSellConditionMet && isProfitGreaterThanPi;
    }
}