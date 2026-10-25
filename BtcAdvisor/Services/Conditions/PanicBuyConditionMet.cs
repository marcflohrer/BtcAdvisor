using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Conditions;

public class PanicBuyConditionMet(AdditionalPanicBuyCondition additionalCondition) : IConditionMet
{
    public TradeAdvice TradeAdvice { get; set; } = TradeAdvice.PanicBuy;

    public bool IsConditionsMet(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? currentPrice,
        decimal? lowestBuyAfterFireSell = null,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell = null)
    {
        var buyConditionMet = new BuyFormulaConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var buy2kConditionMet = new Buy2KConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var strictBuyConditionMet = new StrictBuyConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var strongBuyConditionMet = new StrongBuyConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var whichSabbathBuyConditionMet = new WitchSabbathBuyCondition().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var HiddenBuyConditionMet = new HiddenBuyConditionMet().IsConditionsMet(oscillatorStatistics, movingAverageStatistics, latestBuyPrice, latestSellPrice, currentPrice);
        var isBuyConditionMet = buy2kConditionMet
            || buyConditionMet
            || strictBuyConditionMet
            || strongBuyConditionMet
            || whichSabbathBuyConditionMet
            || HiddenBuyConditionMet;
        
        SetTradeAdvice(buyConditionMet, buy2kConditionMet, strictBuyConditionMet, strongBuyConditionMet, whichSabbathBuyConditionMet, HiddenBuyConditionMet);

        return isBuyConditionMet && additionalCondition.IsConditionsMet(
            latestBuyPrice,
            latestSellPrice,
            currentPrice,
            lowestBuyAfterFireSell,
            highestPanicBuyAfterLowestBuyAfterFireSell);
    }

    private void SetTradeAdvice(bool buyConditionMet, bool buy2kConditionMet, bool strictBuyConditionMet, bool strongBuyConditionMet, bool whichSabbathBuyConditionMet, bool HiddenBuyConditionMet)
    {
        if (buy2kConditionMet)
        {
            TradeAdvice = TradeAdvice.PanicBuy2k;
        }
        else if (buyConditionMet)
        {
            TradeAdvice = TradeAdvice.PanicBuy;
        }
        else if (strictBuyConditionMet)
        {
            TradeAdvice = TradeAdvice.PanicBuyStrict;
        }
        else if (strongBuyConditionMet)
        {
            TradeAdvice = TradeAdvice.PanicBuyStrong;
        }
        else if (whichSabbathBuyConditionMet)
        {
            TradeAdvice = TradeAdvice.PanicBuyWitchSabbath;
        }
        else if (HiddenBuyConditionMet)
        {
            TradeAdvice = TradeAdvice.PanicBuyHidden;
        }
    }
}
