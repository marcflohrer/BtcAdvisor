using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Services;
using BtcAdvisor.Repository;

namespace BtcAdvisor.Worker;

public class BinanceCandleWarmupService(
    IBinanceKlineService binanceKlineService,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private readonly IBinanceKlineService _binanceKlineService = binanceKlineService;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private static readonly string[] MacroIntervals = ["2h", "4h", "12h", "1d", "1w"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureStockCandleSchemaAsync(stoppingToken);

        foreach (var interval in MacroIntervals)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var candles = await _binanceKlineService.GetCandlesUpToAsync(interval, DateTime.UtcNow, stoppingToken);
                Console.WriteLine($"[BinanceCandleWarmupService] Warmed up interval '{interval}' with {candles.Count} candles.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BinanceCandleWarmupService] Warmup failed for interval '{interval}': {ex.Message}");
            }
        }
    }

    private async Task EnsureStockCandleSchemaAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await db.Database.MigrateAsync(stoppingToken);

            Console.WriteLine("[BinanceCandleWarmupService] Applied pending EF Core migrations.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BinanceCandleWarmupService] Migration apply failed: {ex.Message}");
        }
    }
}