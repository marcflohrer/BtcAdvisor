namespace BtcAdvisor.Services;

public interface IIndicatorBackfillService
{
    Task<BackfillOperationResult> BackfillMissingIndicatorsAsync(string interval, IReadOnlyCollection<DateTime> missingTimestampsUtc, CancellationToken cancellationToken);
}