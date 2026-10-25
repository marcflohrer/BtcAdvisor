namespace BtcAdvisor.Services;

public record OhlcvCandle(
    DateTime OpenTime,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume,
    DateTime CloseTime);

public interface IBinanceKlineService
{
    /// <summary>
    /// Returns all cached candles for the given interval whose CloseTime is at or before upTo.
    /// Fetches and caches the full history on first call per interval.
    /// </summary>
    Task<IReadOnlyList<OhlcvCandle>> GetCandlesUpToAsync(string interval, DateTime upTo, CancellationToken cancellationToken);

    /// <summary>
    /// Returns cached candles within the given time range [from, to].
    /// Filters efficiently using binary search; avoids loading unnecessary candles into memory.
    /// </summary>
    Task<IReadOnlyList<OhlcvCandle>> GetCandlesRangeAsync(string interval, DateTime from, DateTime to, CancellationToken cancellationToken);

    /// <summary>
    /// Clears the in-memory cache for one interval so subsequent reads reload from storage.
    /// </summary>
    void InvalidateInterval(string interval);
}
