namespace BtcAdvisor.Services;

/// <summary>Immutable snapshot of the last gap-checker run.</summary>
public sealed record GapCheckerStatusSnapshot(
    DateTime RunStartedUtc,
    DateTime RunFinishedUtc,
    bool WasDryRun,
    int TotalCandleGaps,
    int TotalIndicatorGaps,
    int TotalCandlesFilled,
    int TotalIndicatorsFilled,
    IReadOnlyList<string> Intervals);

/// <summary>
/// Singleton store that tracks the most-recent gap-checker run.
/// Thread-safe via <see cref="Interlocked"/>.
/// </summary>
public sealed class GapCheckerStatusStore
{
    private volatile GapCheckerStatusSnapshot? _latest;
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;
    public GapCheckerStatusSnapshot? Latest => _latest;

    internal void MarkRunning() => _isRunning = true;

    internal void MarkFinished(GapCheckerStatusSnapshot snapshot)
    {
        _latest = snapshot;
        _isRunning = false;
    }
}
