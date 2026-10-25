using Microsoft.Extensions.Caching.Memory;

namespace BtcAdvisor.Services;

public class BearMarketFilterService(
    IBinanceKlineService binanceKlineService,
    ILocalIndicatorComputationService indicatorComputationService,
    IMemoryCache memoryCache) : IBearMarketFilterService
{
    public async Task<bool> IsBearMarketAsync(string interval, DateTime asOfUtc, int period, CancellationToken cancellationToken)
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
        var cacheKey = $"bearfilter:{interval}:{period}:{asOfNormalized:o}";
        if (memoryCache.TryGetValue<bool>(cacheKey, out var cached))
        {
            return cached;
        }

        var candles = await binanceKlineService.GetCandlesUpToAsync(interval, asOfNormalized, cancellationToken);
        var ordered = candles
            .Where(c => c.CloseTime <= asOfNormalized)
            .OrderBy(c => c.CloseTime)
            .ToList();

        if (ordered.Count < period)
        {
            // fail-open: insufficient history → not bear
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var closes = ordered.Select(c => c.Close).ToArray();
        var smaSeries = indicatorComputationService.ComputeSma(closes, period);
        var latestSma = smaSeries[^1];
        if (!latestSma.HasValue)
        {
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var latestClose = closes[^1];
        var isBear = latestClose < latestSma.Value;
        memoryCache.Set(cacheKey, isBear, TimeSpan.FromHours(24));
        return isBear;
    }

    public async Task<bool> IsDeepCycleLowAsync(string interval, DateTime asOfUtc, double drawdownThresholdPercent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            throw new ArgumentException("interval is required", nameof(interval));
        }
        if (drawdownThresholdPercent <= 0 || drawdownThresholdPercent >= 100)
        {
            throw new ArgumentOutOfRangeException(nameof(drawdownThresholdPercent), "must be between 0 and 100 exclusive");
        }

        var asOfNormalized = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var cacheKey = $"deepcyclelow:{interval}:{drawdownThresholdPercent}:{asOfNormalized:o}";
        if (memoryCache.TryGetValue<bool>(cacheKey, out var cached))
        {
            return cached;
        }

        var candles = await binanceKlineService.GetCandlesUpToAsync(interval, asOfNormalized, cancellationToken);
        var ordered = candles
            .Where(c => c.CloseTime <= asOfNormalized)
            .OrderBy(c => c.CloseTime)
            .ToList();

        if (ordered.Count < 2)
        {
            // fail-open: no ATH context yet
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var ath = ordered.Max(c => c.High);
        if (ath <= 0)
        {
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var latestLow = ordered[^1].Low;
        var drawdownPct = (latestLow - ath) / ath * 100.0;
        var isDeepLow = drawdownPct <= -drawdownThresholdPercent;
        memoryCache.Set(cacheKey, isDeepLow, TimeSpan.FromHours(24));
        return isDeepLow;
    }

    public async Task<bool> IsCycleTopOvervaluedAsync(
        string interval,
        DateTime asOfUtc,
        int lookbackDays,
        double minDrawupFromCycleLowPercent,
        double minAthProximityPercent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            throw new ArgumentException("interval is required", nameof(interval));
        }
        if (lookbackDays < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(lookbackDays), "must be at least 2");
        }
        if (minDrawupFromCycleLowPercent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minDrawupFromCycleLowPercent), "must be positive");
        }
        if (minAthProximityPercent <= 0 || minAthProximityPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minAthProximityPercent), "must be in range (0, 100]");
        }

        var asOfNormalized = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var cacheKey = $"cycletop:{interval}:{lookbackDays}:{minDrawupFromCycleLowPercent}:{minAthProximityPercent}:{asOfNormalized:o}";
        if (memoryCache.TryGetValue<bool>(cacheKey, out var cached))
        {
            return cached;
        }

        var candles = await binanceKlineService.GetCandlesUpToAsync(interval, asOfNormalized, cancellationToken);
        var cycleStart = asOfNormalized.AddDays(-lookbackDays);
        var ordered = candles
            .Where(c => c.CloseTime <= asOfNormalized && c.CloseTime >= cycleStart)
            .OrderBy(c => c.CloseTime)
            .ToList();

        if (ordered.Count < 2)
        {
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var cycleLow = ordered.Min(c => c.Low);
        var cycleAth = ordered.Max(c => c.High);
        var latestHigh = ordered[^1].High;

        if (cycleLow <= 0 || cycleAth <= 0 || latestHigh <= 0)
        {
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var drawupPercent = (latestHigh - cycleLow) / cycleLow * 100.0;
        var athProximityPercent = latestHigh / cycleAth * 100.0;
        var isOvervalued = drawupPercent >= minDrawupFromCycleLowPercent
                           && athProximityPercent >= minAthProximityPercent;

        memoryCache.Set(cacheKey, isOvervalued, TimeSpan.FromHours(24));
        return isOvervalued;
    }

    public async Task<bool> IsPiCycleTopAsync(string interval, DateTime asOfUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            throw new ArgumentException("interval is required", nameof(interval));
        }

        var asOfNormalized = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var cacheKey = $"picycletop:{interval}:{asOfNormalized:o}";
        if (memoryCache.TryGetValue<bool>(cacheKey, out var cached))
        {
            return cached;
        }

        const int sma111Period = 111;
        const int sma350Period = 350;

        var candles = await binanceKlineService.GetCandlesUpToAsync(interval, asOfNormalized, cancellationToken);
        var closes = candles
            .Where(c => c.CloseTime <= asOfNormalized)
            .OrderBy(c => c.CloseTime)
            .Select(c => (double)c.Close)
            .ToArray();

        if (closes.Length < sma350Period)
        {
            // Insufficient history — fail-open (do not block)
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var sma111Series = indicatorComputationService.ComputeSma(closes, sma111Period);
        var sma350Series = indicatorComputationService.ComputeSma(closes, sma350Period);
        var sma111 = sma111Series[^1];
        var sma350 = sma350Series[^1];

        if (!sma111.HasValue || !sma350.HasValue || sma350.Value <= 0)
        {
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        // Pi Cycle Top fires when 111DMA ≥ 350DMA × 2
        var result = sma111.Value >= sma350.Value * 2.0;
        memoryCache.Set(cacheKey, result, TimeSpan.FromHours(24));
        return result;
    }

    public async Task<bool> IsPuellMultipleUndervaluedAsync(
        string interval,
        DateTime asOfUtc,
        double threshold,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            throw new ArgumentException("interval is required", nameof(interval));
        }
        if (threshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "threshold must be positive");
        }

        const int smaPeriod = 365;
        var asOfNormalized = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var cacheKey = $"puellmultiple:{interval}:{threshold}:{asOfNormalized:o}";
        if (memoryCache.TryGetValue<bool>(cacheKey, out var cached))
        {
            return cached;
        }

        var candles = await binanceKlineService.GetCandlesUpToAsync(interval, asOfNormalized, cancellationToken);
        var closes = candles
            .Where(c => c.CloseTime <= asOfNormalized)
            .OrderBy(c => c.CloseTime)
            .Select(c => (double)c.Close)
            .ToArray();

        if (closes.Length < smaPeriod)
        {
            // Insufficient history — fail-open (do not block)
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var smaSeries = indicatorComputationService.ComputeSma(closes, smaPeriod);
        var sma365 = smaSeries[^1];

        if (!sma365.HasValue || sma365.Value <= 0)
        {
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        // Puell ≈ currentClose / SMA(365): below threshold = deep accumulation zone
        var puellApprox = closes[^1] / sma365.Value;
        var isUndervalued = puellApprox < threshold;
        memoryCache.Set(cacheKey, isUndervalued, TimeSpan.FromHours(24));
        return isUndervalued;
    }

    public async Task<bool> IsAthProximityTooHighForBuyAsync(
        string interval,
        DateTime asOfUtc,
        double maxAthProximityPercent,
        int maxAthAgeDays,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            throw new ArgumentException("interval is required", nameof(interval));
        }
        if (maxAthProximityPercent <= 0 || maxAthProximityPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAthProximityPercent), "must be in range (0, 100]");
        }

        var asOfNormalized = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var cacheKey = $"athproximity:{interval}:{maxAthProximityPercent}:{maxAthAgeDays}:{asOfNormalized:o}";
        if (memoryCache.TryGetValue<bool>(cacheKey, out var cached))
        {
            return cached;
        }

        var candles = await binanceKlineService.GetCandlesUpToAsync(interval, asOfNormalized, cancellationToken);
        var ordered = candles
            .Where(c => c.CloseTime <= asOfNormalized)
            .OrderBy(c => c.CloseTime)
            .ToList();

        if (ordered.Count < 2)
        {
            // fail-open: insufficient history → do not block
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var athCandle = ordered.MaxBy(c => c.High)!;
        if (athCandle.High <= 0)
        {
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var latestClose = ordered[^1].Close;
        var proximityPercent = (double)latestClose / (double)athCandle.High * 100.0;
        if (proximityPercent <= maxAthProximityPercent)
        {
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        // ATH age check: if the ATH was set long ago (pre-breakout accumulation), do not block.
        if (maxAthAgeDays > 0)
        {
            var athAgeDays = (asOfNormalized - athCandle.CloseTime).TotalDays;
            if (athAgeDays > maxAthAgeDays)
            {
                memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
                return false;
            }
        }

        memoryCache.Set(cacheKey, true, TimeSpan.FromHours(24));
        return true;
    }

    public async Task<bool> IsNearAthForSellSuppressAsync(
        string interval,
        DateTime asOfUtc,
        double minAthProximityPercent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            throw new ArgumentException("interval is required", nameof(interval));
        }
        if (minAthProximityPercent <= 0 || minAthProximityPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minAthProximityPercent), "must be in range (0, 100]");
        }

        var asOfNormalized = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var cacheKey = $"nearathsellsuppress:{interval}:{minAthProximityPercent}:{asOfNormalized:o}";
        if (memoryCache.TryGetValue<bool>(cacheKey, out var cached))
        {
            return cached;
        }

        var candles = await binanceKlineService.GetCandlesUpToAsync(interval, asOfNormalized, cancellationToken);
        var ordered = candles
            .Where(c => c.CloseTime <= asOfNormalized)
            .OrderBy(c => c.CloseTime)
            .ToList();

        if (ordered.Count < 2)
        {
            // fail-open: insufficient history → do not suppress
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var athHigh = ordered.Max(c => c.High);
        if (athHigh <= 0)
        {
            memoryCache.Set(cacheKey, false, TimeSpan.FromHours(24));
            return false;
        }

        var latestClose = ordered[^1].Close;
        var proximityPercent = (double)latestClose / (double)athHigh * 100.0;
        var result = proximityPercent >= minAthProximityPercent;
        memoryCache.Set(cacheKey, result, TimeSpan.FromHours(24));
        return result;
    }

    public async Task<double> GetCycleAthAsync(
        string interval,
        DateTime asOfUtc,
        int lookbackDays,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interval))
            throw new ArgumentException("interval is required", nameof(interval));
        if (lookbackDays < 2)
            throw new ArgumentOutOfRangeException(nameof(lookbackDays), "must be at least 2");

        var asOfNormalized = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var cacheKey = $"cycleAth:{interval}:{lookbackDays}:{asOfNormalized:o}";
        if (memoryCache.TryGetValue<double>(cacheKey, out var cached))
            return cached;

        var candles = await binanceKlineService.GetCandlesUpToAsync(interval, asOfNormalized, cancellationToken);
        var cycleStart = asOfNormalized.AddDays(-lookbackDays);
        var window = candles
            .Where(c => c.CloseTime <= asOfNormalized && c.CloseTime >= cycleStart)
            .ToList();

        if (window.Count < 2)
        {
            memoryCache.Set(cacheKey, 0.0, TimeSpan.FromHours(24));
            return 0.0;
        }

        var ath = window.Max(c => c.High);
        memoryCache.Set(cacheKey, ath, TimeSpan.FromHours(24));
        return ath;
    }
}
