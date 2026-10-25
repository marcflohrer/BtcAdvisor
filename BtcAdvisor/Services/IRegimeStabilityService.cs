using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public interface IRegimeStabilityService
{
    /// <summary>
    /// Laeuft die Strategie mit fixen Produktions-Parametern ueber mehrere rollende Zeitfenster
    /// und misst Konsistenz. Beantwortet: "Funktioniert die Strategie in verschiedenen Marktphasen
    /// oder nur in einer gluecklichen Periode?"
    /// </summary>
    Task<RegimeStabilityResult> RunAsync(
        DateTime startDate,
        DateTime endDate,
        int windowMonths,
        int stepMonths,
        CancellationToken cancellationToken);
}
