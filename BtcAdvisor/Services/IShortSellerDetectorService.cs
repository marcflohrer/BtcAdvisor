using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public interface IShortSellerDetectorService
{
    /// <summary>
    /// Ermittelt die aktuelle ShortSeller-Phase und persistiert einen neuen State,
    /// wenn sich die Phase gegenüber dem letzten gespeicherten State geändert hat.
    /// </summary>
    Task DetectAndPersistAsync (CancellationToken ct = default);

    /// <summary>
    /// Gets the short seller phase.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<ShortSellerPhase> GetShortSellerPhase (CancellationToken ct);
}
