namespace BtcAdvisor.Services;

/// <summary>Options for the gap checker background worker, bound from appsettings "GapChecker".</summary>
public sealed class GapCheckerOptions
{
    public const string SectionName = "GapChecker";

    /// <summary>How often the background worker runs its full scan cycle, in hours. Default: 1.</summary>
    public double PeriodicIntervalHours { get; set; } = 1.0;

    /// <summary>
    /// Per-interval lookback overrides in days.
    /// Keys are Binance interval strings (e.g. "1d", "4h").
    /// Any interval not listed here falls back to <see cref="BackfillTimeframeCatalog.GetDefaultLookbackDays"/>.
    /// </summary>
    public Dictionary<string, int> LookbackDays { get; set; } = [];
}
