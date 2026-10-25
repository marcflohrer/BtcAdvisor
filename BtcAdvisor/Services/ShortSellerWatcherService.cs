namespace BtcAdvisor.Services;

public class ShortSellerWatcherService(IServiceScopeFactory scopeFactory, ILogger<ShortSellerWatcherService> log) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<ShortSellerWatcherService> _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var detector = scope.ServiceProvider.GetRequiredService<IShortSellerDetectorService>();
                var alerter = scope.ServiceProvider.GetRequiredService<IShortSellerAlertService>();

                await detector.DetectAndPersistAsync(stoppingToken);
                await alerter.NotifyIfPhaseChangedAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "ShortSellerWatcher tick failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}