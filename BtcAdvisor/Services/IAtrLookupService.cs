namespace BtcAdvisor.Services;

/// <summary>
/// Resolves the Average True Range (ATR) value of BTC/USDT at a given timestamp.
/// Used by risk-control logic to compute volatility-aware stop-loss levels.
/// </summary>
public interface IAtrLookupService
{
    /// <summary>
    /// Returns the most recent ATR value at or before <paramref name="asOfUtc"/> for the
    /// requested kline interval (e.g. "1d", "1h"), or null if not enough data is available.
    /// </summary>
    Task<double?> GetAtrAsync(string interval, DateTime asOfUtc, int period, CancellationToken cancellationToken);
}
