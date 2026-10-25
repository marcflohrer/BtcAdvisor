using BtcAdvisor.Services.Models;

public class AdditionalPanicBuyCondition
{
    public bool IsConditionsMet(
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? currentPrice,
        decimal? lowestBuyAfterFireSell = null,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell = null)
    {
        var profit = Convert.ToDouble(latestSellPrice) - Convert.ToDouble(currentPrice);
        var percentage = PercentageService.GetPercentage(profit, Convert.ToDouble(currentPrice));
        // When there is no sell history, the price-drop condition is considered met
        var isProfitGreaterThanThreshold = latestSellPrice == null || percentage > double.Pi;

        var increaseSinceLatestDrop = Convert.ToDouble(currentPrice) - Convert.ToDouble(lowestBuyAfterFireSell);
        var increaseSinceLatestDropInPercent = PercentageService.GetPercentage(increaseSinceLatestDrop, Convert.ToDouble(lowestBuyAfterFireSell));
        // When there is no lowest-buy-after-fire-sell, the increase condition is considered met
        var increaseSinceLatestDropInPercentGreaterThanThreshold = lowestBuyAfterFireSell == null || increaseSinceLatestDropInPercent > double.Pi;

        var increaseSinceHighestPanicBuyInPercentGreaterThanThreshold = true;
        if (highestPanicBuyAfterLowestBuyAfterFireSell is not null)
        {
            var increaseSinceHighestPanicBuy = Convert.ToDouble(currentPrice) - Convert.ToDouble(highestPanicBuyAfterLowestBuyAfterFireSell);
            var increaseSinceHighestPanicBuyInPercent = PercentageService.GetPercentage(increaseSinceHighestPanicBuy, Convert.ToDouble(highestPanicBuyAfterLowestBuyAfterFireSell));
            increaseSinceHighestPanicBuyInPercentGreaterThanThreshold = increaseSinceHighestPanicBuyInPercent > double.Pi;
        }

        return isProfitGreaterThanThreshold 
            && increaseSinceLatestDropInPercentGreaterThanThreshold
            && increaseSinceHighestPanicBuyInPercentGreaterThanThreshold;
    }
}