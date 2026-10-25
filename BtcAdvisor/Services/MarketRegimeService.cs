using Microsoft.Extensions.Caching.Memory;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public class MarketRegimeService(
    IBinanceKlineService binanceKlineService,
    ILocalIndicatorComputationService indicatorComputationService,
    IMemoryCache memoryCache) : IMarketRegimeService
{
    public async Task<MarketRegimeSnapshot> GetRegimeAsync(DateTime asOf, CancellationToken cancellationToken)
    {
        var asOfUtc = asOf.ToUniversalTime();
        var cacheKey = $"market-regime:{asOfUtc:o}";
        if (memoryCache.TryGetValue<MarketRegimeSnapshot>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var candles = await binanceKlineService.GetCandlesUpToAsync("1d", asOfUtc, cancellationToken);
        var relevantCandles = candles
            .Where(c => c.CloseTime <= asOfUtc)
            .OrderBy(c => c.CloseTime)
            .ToList();

        if (relevantCandles.Count == 0)
        {
            var emptySnapshot = new MarketRegimeSnapshot(MarketRegime.Neutral, 0d, null, null, null);
            memoryCache.Set(cacheKey, emptySnapshot, TimeSpan.FromHours(24));
            return emptySnapshot;
        }

        var closes = relevantCandles.Select(c => c.Close).ToList();
        var highs = relevantCandles.Select(c => c.High).ToList();
        var lows = relevantCandles.Select(c => c.Low).ToList();

        var ema50 = indicatorComputationService.ComputeEma(closes, MarketRegimeThresholds.EmaFastPeriod);
        var ema200 = indicatorComputationService.ComputeEma(closes, MarketRegimeThresholds.EmaSlowPeriod);
        var adx = indicatorComputationService.ComputeAdx(highs, lows, closes, MarketRegimeThresholds.AdxPeriod);

        var lastIndex = relevantCandles.Count - 1;
        var close = closes[lastIndex];
        var ema50Value = ema50[lastIndex];
        var ema200Value = ema200[lastIndex];
        var adxValue = adx[lastIndex];

        var regime = DetermineRegime(close, ema50Value, ema200Value, adxValue);
        var snapshot = new MarketRegimeSnapshot(regime, close, ema50Value, ema200Value, adxValue);
        memoryCache.Set(cacheKey, snapshot, TimeSpan.FromHours(24));
        return snapshot;
    }

    private static MarketRegime DetermineRegime(double close, double? ema50, double? ema200, double? adx)
    {
        if (!ema50.HasValue || !ema200.HasValue || !adx.HasValue)
        {
            return MarketRegime.Neutral;
        }

        if (adx.Value <= MarketRegimeThresholds.AdxTrendThreshold)
        {
            return MarketRegime.Neutral;
        }

        if (close > ema200.Value && ema50.Value > ema200.Value)
        {
            return MarketRegime.Bull;
        }

        if (close < ema200.Value && ema50.Value < ema200.Value)
        {
            return MarketRegime.Bear;
        }

        return MarketRegime.Neutral;
    }
}