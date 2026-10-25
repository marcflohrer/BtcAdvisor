namespace BtcAdvisor.Services;

public sealed record MissingTimestampRange(
    DateTime StartUtc,
    DateTime EndUtc,
    int MissingCount);

public sealed record TimeframeGapCheckResult(
    string Interval,
    string Symbol,
    int PeriodMinutes,
    int ExpectedCount,
    int ActualCount,
    IReadOnlyList<DateTime> MissingTimestampsUtc,
    IReadOnlyList<MissingTimestampRange> MissingRanges);

public sealed record BackfillOperationResult(
    string Interval,
    int RequestedCount,
    int FilledCount,
    IReadOnlyList<DateTime> FilledTimestampsUtc,
    IReadOnlyList<DateTime> SkippedTimestampsUtc);

public sealed record GapBackfillRunResult(
    DateTime StartUtc,
    DateTime EndUtc,
    bool AutoBackfill,
    IReadOnlyList<TimeframeGapCheckResult> CandleChecks,
    IReadOnlyList<TimeframeGapCheckResult> IndicatorChecks,
    IReadOnlyList<BackfillOperationResult> CandleBackfills,
    IReadOnlyList<BackfillOperationResult> IndicatorBackfills);