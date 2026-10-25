using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

/// <summary>
/// Stationary-Block-Bootstrap (Politis &amp; Romano 1994) der taeglichen Strategie-Renditen.
/// Liefert Konfidenzintervalle und Alpha-p-Wert unter Erhalt der Autokorrelationsstruktur.
/// </summary>
public interface IStationaryBootstrapService
{
    Task<BacktestBootstrapResult> RunAsync(
        DateTime startDate,
        DateTime endDate,
        int iterations,
        int meanBlockLength,
        int? seed,
        CancellationToken cancellationToken);
}
