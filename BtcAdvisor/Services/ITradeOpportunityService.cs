using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public interface ITradeOpportunityService
{
    public Task TradeOpportunity(Deal deal, CancellationToken cancellationToken);
}