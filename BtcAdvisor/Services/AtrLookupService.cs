using Microsoft.Extensions.Caching.Memory;

namespace BtcAdvisor.Services;

public class AtrLookupService(
    IBinanceKlineService binanceKlineService,
    ILocalIndicatorComputationService indicatorComputationService,
    IMemoryCache memoryCache) : IAtrLookupService
{
    public async Task<double?> GetAtrAsync(string interval, DateTime asOfUtc, int period, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            throw new ArgumentException("interval is required", nameof(interval));
        }
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "period must be positive");
        }

        var asOfNormalized = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var cacheKey = $"atr:{interval}:{period}:{asOfNormalized:o}";
        if (memoryCache.TryGetValue<double?>(cacheKey, out var cached))
        {
            return cached;
        }

        var candles = await binanceKlineService.GetCandlesUpToAsync(interval, asOfNormalized, cancellationToken);
        if (candles.Count <= period)
        {
            memoryCache.Set(cacheKey, (double?)null, TimeSpan.FromHours(24));
            return null;
        }

        var ordered = candles
            .Where(c => c.CloseTime <= asOfNormalized)
            .OrderBy(c => c.CloseTime)
            .ToList();

        if (ordered.Count <= period)
        {
            memoryCache.Set(cacheKey, (double?)null, TimeSpan.FromHours(24));
            return null;
        }

        var highs = ordered.Select(c => c.High).ToArray();
        var lows = ordered.Select(c => c.Low).ToArray();
        var closes = ordered.Select(c => c.Close).ToArray();

        var atrSeries = indicatorComputationService.ComputeAtr(highs, lows, closes, period);
        var latest = atrSeries[^1];

        memoryCache.Set(cacheKey, latest, TimeSpan.FromHours(24));
        return latest;
    }
}
