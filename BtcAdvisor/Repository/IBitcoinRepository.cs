using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Repository;

public interface IBitcoinRepository
{
    public Task SaveIndicatorData (IndicatorDatum indicatorDatum, CancellationToken cancellationToken);

    public Task<IndicatorDatumCurrentPrevious> GetIndicatorStatistics (DateTime dateTime, string timeSpan, string indicator, int? period, CancellationToken cancellationToken);

    /// <summary>Gibt exakte IndicatorData-Timestamps im angefragten Zeitraum zurück, optional gefiltert nach Periode.</summary>
    public Task<IReadOnlyCollection<DateTime>> GetDistinctTimestampsAsync(DateTime startDate, DateTime endDate, int? periodMinutes = null, CancellationToken cancellationToken = default);

    public Task<IReadOnlyCollection<DateTime>> GetDistinctTimestampsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken);
}
