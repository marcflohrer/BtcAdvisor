using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public class MarketContextService(
    IBinanceKlineService binanceKlineService,
    ILocalIndicatorComputationService indicatorComputationService,
    IOptions<VolumeVolatilityOptions> options,
    IMemoryCache memoryCache) : IMarketContextService
{
    public async Task<MarketContextSnapshot> GetSnapshotAsync(string interval, DateTime asOfUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            throw new ArgumentException("interval is required", nameof(interval));
        }

        var opts = options.Value;
        var asOfNormalized = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var cacheKey = $"market-context:{interval}:{opts.BbPeriod}:{opts.BbStdDev}:{opts.BbWidthMaPeriod}:{opts.ObvSlopeWindow}:{opts.VwapPeriod}:{opts.VwapReclaimLookback}:{opts.MfiPeriod}:{asOfNormalized:o}";
        if (memoryCache.TryGetValue<MarketContextSnapshot>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var candles = await binanceKlineService.GetCandlesUpToAsync(interval, asOfNormalized, cancellationToken);
        var ordered = candles
            .Where(c => c.CloseTime <= asOfNormalized)
            .OrderBy(c => c.CloseTime)
            .ToList();

        double? bbWidthPercent = null;
        double? bbWidthMaPercent = null;
        double? obvSlopePercent = null;
        double? vwapDistancePercent = null;
        bool vwapReclaimed = false;
        double? mfiValue = null;

        if (ordered.Count >= opts.BbPeriod)
        {
            var closes = ordered.Select(c => c.Close).ToArray();
            var bb = indicatorComputationService.ComputeBollinger(closes, opts.BbPeriod, opts.BbStdDev);
            bbWidthPercent = bb[^1].WidthPercent;

            // BB width MA: average of last BbWidthMaPeriod widths (skip nulls)
            if (opts.BbWidthMaPeriod > 0 && bb.Count >= opts.BbWidthMaPeriod)
            {
                double sum = 0d;
                int count = 0;
                for (int i = bb.Count - opts.BbWidthMaPeriod; i < bb.Count; i++)
                {
                    if (i >= 0 && bb[i].WidthPercent.HasValue)
                    {
                        sum += bb[i].WidthPercent!.Value;
                        count++;
                    }
                }
                if (count > 0)
                {
                    bbWidthMaPercent = sum / count;
                }
            }

            if (opts.ObvSlopeWindow > 1 && ordered.Count > opts.ObvSlopeWindow)
            {
                var volumes = ordered.Select(c => c.Volume).ToArray();
                var obv = indicatorComputationService.ComputeObv(closes, volumes);
                var lastIdx = obv.Count - 1;
                var prevIdx = lastIdx - opts.ObvSlopeWindow;
                if (prevIdx >= 0)
                {
                    var baseline = Math.Abs(obv[prevIdx]);
                    if (baseline > 0d)
                    {
                        obvSlopePercent = (obv[lastIdx] - obv[prevIdx]) / baseline * 100d;
                    }
                    else
                    {
                        // Fall back to absolute delta normalized by sum of volumes in window to avoid div/0
                        double volSum = 0d;
                        for (int i = prevIdx + 1; i <= lastIdx; i++) volSum += Math.Abs(volumes[i]);
                        if (volSum > 0d)
                        {
                            obvSlopePercent = (obv[lastIdx] - obv[prevIdx]) / volSum * 100d;
                        }
                    }
                }
            }
        }

        // VWAP & MFI both need OHLCV
        if (ordered.Count > 0)
        {
            var highs = ordered.Select(c => c.High).ToArray();
            var lows = ordered.Select(c => c.Low).ToArray();
            var closesAll = ordered.Select(c => c.Close).ToArray();
            var volumesAll = ordered.Select(c => c.Volume).ToArray();
            var lastIdx = ordered.Count - 1;
            var lastClose = closesAll[lastIdx];

            if (opts.VwapPeriod > 0 && ordered.Count >= opts.VwapPeriod)
            {
                var vwap = indicatorComputationService.ComputeVwap(highs, lows, closesAll, volumesAll, opts.VwapPeriod);
                var vwapNow = vwap[lastIdx];
                if (vwapNow.HasValue && vwapNow.Value > 0d)
                {
                    vwapDistancePercent = (lastClose - vwapNow.Value) / vwapNow.Value * 100d;

                    // VwapReclaimed: was below VWAP at any point during the lookback and is now above.
                    if (opts.VwapReclaimLookback > 0 && lastClose > vwapNow.Value)
                    {
                        var start = Math.Max(0, lastIdx - opts.VwapReclaimLookback);
                        for (int i = start; i < lastIdx; i++)
                        {
                            if (vwap[i].HasValue && closesAll[i] < vwap[i]!.Value)
                            {
                                vwapReclaimed = true;
                                break;
                            }
                        }
                    }
                }
            }

            if (opts.MfiPeriod > 0 && ordered.Count > opts.MfiPeriod)
            {
                var mfi = indicatorComputationService.ComputeMfi(highs, lows, closesAll, volumesAll, opts.MfiPeriod);
                mfiValue = mfi[lastIdx];
            }
        }

        var snapshot = new MarketContextSnapshot(bbWidthPercent, bbWidthMaPercent, obvSlopePercent, vwapDistancePercent, vwapReclaimed, mfiValue);
        memoryCache.Set(cacheKey, snapshot, TimeSpan.FromHours(24));
        return snapshot;
    }
}
