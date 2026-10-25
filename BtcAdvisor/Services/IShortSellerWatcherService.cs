namespace BtcAdvisor.Services;

public interface IShortSellerWatcherService
{
    public Task WatchForShortSellersAsync(CancellationToken stoppingToken);
}