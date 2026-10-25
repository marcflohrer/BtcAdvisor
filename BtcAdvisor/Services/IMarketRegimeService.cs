using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public interface IMarketRegimeService
{
    Task<MarketRegimeSnapshot> GetRegimeAsync(DateTime asOf, CancellationToken cancellationToken);
}