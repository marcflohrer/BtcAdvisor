namespace BtcAdvisor.Services;

public interface ICandleBackfillService
{
    Task<BackfillOperationResult> BackfillMissingCandlesAsync(string interval, IReadOnlyCollection<DateTime> missingTimestampsUtc, CancellationToken cancellationToken);
}