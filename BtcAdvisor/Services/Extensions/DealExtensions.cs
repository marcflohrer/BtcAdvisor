using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Extensions;

namespace BtcAdvisor.Services.Extensions;

public static class DealExtensions
{
    public static ProfitAndBuyDeal GetProfit(this Deal deal, BuyActionLog? previousBuyActionLog)
    {
        var profit = string.Empty;
        if (previousBuyActionLog != null)
        {
            var delta = deal.Price - previousBuyActionLog.Price;
            var profitOrLoss = delta > 0 ? "Gewinn" : "Verlust";
            if(delta == 0)
            {
                profitOrLoss = "Nullsummenspiel";
            }
            var localTime = previousBuyActionLog.TradingDate.ToCetDateTime();
            profit = $" {profitOrLoss}: $ {delta} seit Kauf: {localTime}";
        }
        return new ProfitAndBuyDeal(profit, previousBuyActionLog);
    }
}