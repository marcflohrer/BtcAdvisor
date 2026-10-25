using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public interface IMonteCarloSignificanceService
{
    /// <summary>
    /// Fuehrt einen Random-Entry Monte-Carlo-Test durch: 1) Realer Backtest liefert die echten Deals,
    /// 2) je Iteration werden N zufaellige Buy-Zeitpunkte aus dem 2h-Kerzen-Grid gezogen mit identischer
    /// Haltedauer wie die realen Deals, 3) p-Wert = Anteil zufaelliger Runs, die mindestens so gut sind
    /// wie die reale Strategie.
    /// </summary>
    Task<BacktestMonteCarloResult> RunAsync(
        DateTime startDate,
        DateTime endDate,
        int iterations,
        int? seed,
        CancellationToken cancellationToken);
}
