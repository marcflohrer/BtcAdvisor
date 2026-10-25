namespace BtcAdvisor.Services;

public sealed class GapBackfillCoordinator(
    IGapDetectionService gapDetectionService,
    ICandleBackfillService candleBackfillService,
    IIndicatorBackfillService indicatorBackfillService) : IGapBackfillCoordinator
{
    private readonly IGapDetectionService _gapDetectionService = gapDetectionService;
    private readonly ICandleBackfillService _candleBackfillService = candleBackfillService;
    private readonly IIndicatorBackfillService _indicatorBackfillService = indicatorBackfillService;

    public async Task<GapBackfillRunResult> ScanAndBackfillAsync(
        DateTime startUtc,
        DateTime endUtc,
        IReadOnlyCollection<string>? intervals,
        bool autoBackfill,
        CancellationToken cancellationToken)
    {
        var normalizedStart = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var normalizedEnd = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
        var effectiveIntervals = intervals is { Count: > 0 }
            ? intervals.Distinct(StringComparer.Ordinal).ToArray()
            : BackfillTimeframeCatalog.SupportedIntervals;

        var candleChecks = new List<TimeframeGapCheckResult>(effectiveIntervals.Count);
        var indicatorChecks = new List<TimeframeGapCheckResult>(effectiveIntervals.Count);
        var candleBackfills = new List<BackfillOperationResult>();
        var indicatorBackfills = new List<BackfillOperationResult>();

        foreach (var interval in effectiveIntervals)
        {
            var candleCheck = await _gapDetectionService.DetectCandleGapsAsync(interval, normalizedStart, normalizedEnd, cancellationToken);
            candleChecks.Add(candleCheck);

            var indicatorCheck = await _gapDetectionService.DetectIndicatorGapsAsync(interval, normalizedStart, normalizedEnd, cancellationToken);
            indicatorChecks.Add(indicatorCheck);

            if (!autoBackfill)
            {
                continue;
            }

            if (candleCheck.MissingTimestampsUtc.Count > 0)
            {
                candleBackfills.Add(await _candleBackfillService.BackfillMissingCandlesAsync(interval, candleCheck.MissingTimestampsUtc, cancellationToken));
            }

            if (indicatorCheck.MissingTimestampsUtc.Count > 0)
            {
                indicatorBackfills.Add(await _indicatorBackfillService.BackfillMissingIndicatorsAsync(interval, indicatorCheck.MissingTimestampsUtc, cancellationToken));
            }
        }

        return new GapBackfillRunResult(
            normalizedStart,
            normalizedEnd,
            autoBackfill,
            candleChecks,
            indicatorChecks,
            candleBackfills,
            indicatorBackfills);
    }
}