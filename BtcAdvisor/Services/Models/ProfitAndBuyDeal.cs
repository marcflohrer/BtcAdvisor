using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Models;

public record ProfitAndBuyDeal(string Profit, BuyActionLog? BuyActionLog);