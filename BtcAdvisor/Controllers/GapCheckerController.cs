using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using BtcAdvisor.Services;

namespace BtcAdvisor.Controllers;

[ApiController]
[Route("api/gap-checker")]
public class GapCheckerController(
    IGapBackfillCoordinator gapBackfillCoordinator,
    GapCheckerStatusStore statusStore,
    IOptions<GapCheckerOptions> options) : ControllerBase
{
    private readonly IGapBackfillCoordinator _gapBackfillCoordinator = gapBackfillCoordinator;
    private readonly GapCheckerStatusStore _statusStore = statusStore;
    private readonly GapCheckerOptions _options = options.Value;

    /// <summary>
    /// Runs a gap scan (and optionally backfill) for the specified window.
    /// Use dryRun=true to only report gaps without writing to the database.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<GapBackfillRunResult>> Run(
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] bool dryRun = false,
        [FromQuery] string[]? intervals = null,
        CancellationToken cancellationToken = default)
    {
        var end = (endUtc ?? DateTime.UtcNow).ToUniversalTime();
        var start = (startUtc ?? end.AddDays(-30)).ToUniversalTime();

        if (end <= start)
        {
            return BadRequest("endUtc must be greater than startUtc.");
        }

        if (intervals is { Length: > 0 } && intervals.Any(i => !BackfillTimeframeCatalog.SupportedIntervals.Contains(i)))
        {
            return BadRequest($"intervals must be a subset of: {string.Join(", ", BackfillTimeframeCatalog.SupportedIntervals)}");
        }

        _statusStore.MarkRunning();
        var runStarted = DateTime.UtcNow;

        var result = await _gapBackfillCoordinator.ScanAndBackfillAsync(
            start,
            end,
            intervals,
            autoBackfill: !dryRun,
            cancellationToken);

        var snapshot = new GapCheckerStatusSnapshot(
            RunStartedUtc: runStarted,
            RunFinishedUtc: DateTime.UtcNow,
            WasDryRun: dryRun,
            TotalCandleGaps: result.CandleChecks.Sum(c => c.MissingTimestampsUtc.Count),
            TotalIndicatorGaps: result.IndicatorChecks.Sum(c => c.MissingTimestampsUtc.Count),
            TotalCandlesFilled: result.CandleBackfills.Sum(b => b.FilledCount),
            TotalIndicatorsFilled: result.IndicatorBackfills.Sum(b => b.FilledCount),
            Intervals: result.CandleChecks.Select(c => c.Interval).ToArray());

        _statusStore.MarkFinished(snapshot);

        return Ok(result);
    }

    /// <summary>Returns metadata about the last gap-checker run.</summary>
    [HttpGet("status")]
    public ActionResult<GapCheckerStatusResponse> Status()
    {
        var latest = _statusStore.Latest;
        return Ok(new GapCheckerStatusResponse(
            IsRunning: _statusStore.IsRunning,
            SupportedIntervals: BackfillTimeframeCatalog.SupportedIntervals,
            DefaultLookbackDays: BackfillTimeframeCatalog.SupportedIntervals
                .ToDictionary(
                    i => i,
                    i => _options.LookbackDays.TryGetValue(i, out var d) && d > 0
                        ? d
                        : BackfillTimeframeCatalog.GetDefaultLookbackDays(i)),
            PeriodicIntervalHours: _options.PeriodicIntervalHours,
            LastRun: latest));
    }
}

public sealed record GapCheckerStatusResponse(
    bool IsRunning,
    IReadOnlyList<string> SupportedIntervals,
    IReadOnlyDictionary<string, int> DefaultLookbackDays,
    double PeriodicIntervalHours,
    GapCheckerStatusSnapshot? LastRun);
