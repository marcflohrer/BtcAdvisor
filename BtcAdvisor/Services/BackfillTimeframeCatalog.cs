using BtcAdvisor.Constants;

namespace BtcAdvisor.Services;

public static class BackfillTimeframeCatalog
{
    public static IReadOnlyList<string> SupportedIntervals => BitcoinConstants.MapPeriodToMinutes
        .OrderBy(kvp => kvp.Value)
        .Select(kvp => kvp.Key)
        .ToArray();

    public static string GetDatabaseSymbol(string interval) => interval switch
    {
        "5m" => "BTC5M",
        "15m" => "BTC15M",
        "30m" => "BTC30M",
        "1h" => "BTC1H",
        "2h" => "BTC2H",
        "4h" => "BTC4H",
        "12h" => "BTC12H",
        "1d" => "BTC1D",
        "1w" => "BTC1W",
        _ => throw new ArgumentException($"Unsupported interval: {interval}", nameof(interval))
    };

    public static int GetPeriodMinutes(string interval)
    {
        if (!BitcoinConstants.MapPeriodToMinutes.TryGetValue(interval, out var minutes))
        {
            throw new ArgumentException($"Unsupported interval: {interval}", nameof(interval));
        }

        return minutes;
    }

    public static TimeSpan GetIntervalDuration(string interval) => TimeSpan.FromMinutes(GetPeriodMinutes(interval));

    /// <summary>Returns the built-in default lookback in days for an interval.</summary>
    public static int GetDefaultLookbackDays(string interval) => interval switch
    {
        "5m" => 5,
        "15m" => 14,
        "30m" => 30,
        "1h" => 45,
        "2h" => 90,
        "4h" => 180,
        "12h" => 365,
        "1d" => 730,
        "1w" => 3650,
        _ => throw new ArgumentException($"Unsupported interval: {interval}", nameof(interval))
    };

    /// <summary>
    /// Returns the effective lookback for <paramref name="interval"/>,
    /// using the override from <paramref name="options"/> if present.
    /// </summary>
    public static TimeSpan GetWorkerLookback(string interval, GapCheckerOptions? options = null)
    {
        if (options?.LookbackDays.TryGetValue(interval, out var overrideDays) is true && overrideDays > 0)
        {
            return TimeSpan.FromDays(overrideDays);
        }

        return TimeSpan.FromDays(GetDefaultLookbackDays(interval));
    }
}