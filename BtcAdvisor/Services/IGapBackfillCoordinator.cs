namespace BtcAdvisor.Services;

public interface IGapBackfillCoordinator
{
    Task<GapBackfillRunResult> ScanAndBackfillAsync(
        DateTime startUtc,
        DateTime endUtc,
        IReadOnlyCollection<string>? intervals,
        bool autoBackfill,
        CancellationToken cancellationToken);
}