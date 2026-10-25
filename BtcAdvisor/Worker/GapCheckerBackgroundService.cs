using Microsoft.Extensions.Options;
using BtcAdvisor.Services;

namespace BtcAdvisor.Worker;

public sealed class GapCheckerBackgroundService(
    IServiceScopeFactory serviceScopeFactory,
    GapCheckerStatusStore statusStore,
    IOptions<GapCheckerOptions> options) : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly GapCheckerStatusStore _statusStore = statusStore;
    private readonly GapCheckerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _statusStore.MarkRunning();
            var runStarted = DateTime.UtcNow;

            foreach (var interval in BackfillTimeframeCatalog.SupportedIntervals)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var coordinator = scope.ServiceProvider.GetRequiredService<IGapBackfillCoordinator>();
                    var endUtc = DateTime.UtcNow;
                    var startUtc = endUtc - BackfillTimeframeCatalog.GetWorkerLookback(interval, _options);
                    await coordinator.ScanAndBackfillAsync(
                        startUtc,
                        endUtc,
                        [interval],
                        autoBackfill: true,
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GapCheckerBackgroundService] {interval} failed: {ex.Message}");
                }
            }

            // Update status as finished (summary-level; detailed results not aggregated here)
            _statusStore.MarkFinished(new GapCheckerStatusSnapshot(
                RunStartedUtc: runStarted,
                RunFinishedUtc: DateTime.UtcNow,
                WasDryRun: false,
                TotalCandleGaps: 0,
                TotalIndicatorGaps: 0,
                TotalCandlesFilled: 0,
                TotalIndicatorsFilled: 0,
                Intervals: BackfillTimeframeCatalog.SupportedIntervals));

            var delay = TimeSpan.FromHours(Math.Max(0.1, _options.PeriodicIntervalHours));
            await Task.Delay(delay, stoppingToken);
        }
    }
}
