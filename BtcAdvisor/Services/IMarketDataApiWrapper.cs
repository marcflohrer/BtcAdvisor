using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public interface IMarketDataApiWrapper
{
    Task<MarketDataApiResponse?> GetStockPrice (string symbol, DateTime endTime, CancellationToken cancellationToken);
}
