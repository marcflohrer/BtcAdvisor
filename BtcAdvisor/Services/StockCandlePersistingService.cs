using BtcAdvisor.Repository;
using BtcAdvisor.Extensions;
using BtcAdvisor.Services.Extensions;

namespace BtcAdvisor.Services;

public class StockCandlePersistingService(IServiceScopeFactory serviceScopeFactory): IStockCandlePersistingService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private const string MicrostrategySymbol = "MSTR";

    public async Task RequestAndSaveStockPriceAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        try
        {
            var apiWrapper = scope.ServiceProvider.GetRequiredService<IMarketDataApiWrapper>();
            var stockCandleRepository = scope.ServiceProvider.GetRequiredService<IStockCandleRepository>();

            var toUtc = DateTime.UtcNow.ToUtcDateTime();

            var apiResponse = await apiWrapper.GetStockPrice(MicrostrategySymbol, toUtc, stoppingToken);
            if (apiResponse == null)
            {
                Console.WriteLine("::MSTR Market Data response is null: Market is closed.");
                return;
            }

            var stockCandles = apiResponse.ToDatabaseModel(MicrostrategySymbol);
            await stockCandleRepository.SaveAsync(stockCandles, stoppingToken);
        }
        catch(Exception ex)
        {
            Console.WriteLine($"{DateTime.UtcNow} ERROR: RequestAndSaveBitCoinIndicatorsAsync failed. {ex.Message} {ex.StackTrace}");
        }
    }
}