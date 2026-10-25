namespace BtcAdvisor.Services;

public interface IShortSellerAlertService
{
    /// <summary>
    /// Prüft die zwei neuesten ShortSellerStates. Wenn die Phase gewechselt hat und
    /// für den neuesten State noch keine Notification existiert, sendet genau eine Nachricht.
    /// </summary>
    Task NotifyIfPhaseChangedAsync(CancellationToken ct = default);
}
