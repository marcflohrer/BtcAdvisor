using BtcAdvisor.Services;

namespace BtcAdvisor.Worker;

public class BitcoinAdviceBackgroundService(IServiceScopeFactory scopeFactory) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var startTime = DateTimeOffset.Now;
            using (var scope = _scopeFactory.CreateScope())
            {
                var adviceService = scope.ServiceProvider.GetRequiredService<BitcoinAdviceChangeService>();
                await adviceService.SendChangeAlertsToBot(DateTime.UtcNow, stoppingToken);
            }
            var endTime = DateTimeOffset.Now;
            var elapsed = (int)(endTime - startTime).TotalMilliseconds;
            var delayTime = Math.Max(0, 60000 - elapsed);  // Warte mindestens 0 Millisekunden, maximal 4 Sekunden, ziehe die verstrichene Zeit ab
            await Task.Delay(delayTime, stoppingToken); // Pause zwischen den Iterationen
        }
    }
}
