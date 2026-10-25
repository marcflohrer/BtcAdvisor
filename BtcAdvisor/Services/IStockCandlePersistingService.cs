namespace BtcAdvisor.Services;

public interface IStockCandlePersistingService
{
    public Task RequestAndSaveStockPriceAsync(CancellationToken stoppingToken);
}