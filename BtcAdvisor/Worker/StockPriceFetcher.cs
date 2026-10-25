using BtcAdvisor.Services;

namespace BtcAdvisor.Worker;

public class StockPriceFetcher(IStockCandlePersistingService StockCandlePersistingService) : BackgroundService
{
    private readonly IStockCandlePersistingService _stockCandlePersistingService = StockCandlePersistingService;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _stockCandlePersistingService.RequestAndSaveStockPriceAsync(stoppingToken);

            // Warte 1 Minuten bevor die nächste Iteration startet
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
