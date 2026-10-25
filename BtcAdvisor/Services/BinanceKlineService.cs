using System.Globalization;
using System.Text.Json;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;

/// <summary>
/// Fetches OHLCV candles from Binance public klines API and caches the full history per interval.
/// Registered as Singleton so the cache persists across DI scope lifetimes.
/// </summary>
public sealed class BinanceKlineService : IBinanceKlineService, IDisposable
{
    private static readonly DateTime BitstampStartUtc = new(2012, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BinanceStartUtc = new(2017, 8, 17, 0, 0, 0, DateTimeKind.Utc);
    private const int SeedLookbackCandles = 600;

    private readonly HttpClient _httpClient = new() { BaseAddress = new Uri("https://api.binance.com") };
    private readonly Dictionary<string, List<OhlcvCandle>> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IServiceScopeFactory _scopeFactory;

    public BinanceKlineService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<IReadOnlyList<OhlcvCandle>> GetCandlesUpToAsync(string interval, DateTime upTo, CancellationToken cancellationToken)
    {
        var all = await GetOrFetchAllAsync(interval, upTo, cancellationToken);
        // Binary-search end boundary: last candle whose CloseTime <= upTo
        var idx = BinarySearchLastCloseTimeLe(all, upTo);
        if (idx < 0) return [];
        return all.Take(idx + 1).ToList();
    }

    public async Task<IReadOnlyList<OhlcvCandle>> GetCandlesRangeAsync(string interval, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var all = await GetOrFetchAllAsync(interval, to, cancellationToken);
        
        // Binary-search: first candle whose CloseTime >= from
        var fromIdx = BinarySearchFirstCloseTimeGe(all, from);
        if (fromIdx < 0) return [];
        
        // Binary-search: last candle whose CloseTime <= to
        var toIdx = BinarySearchLastCloseTimeLe(all, to);
        if (toIdx < fromIdx) return [];
        
        return all.Skip(fromIdx).Take(toIdx - fromIdx + 1).ToList();
    }

    public void InvalidateInterval(string interval)
    {
        _lock.Wait();
        try
        {
            _cache.Remove(interval);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── internal ───────────────────────────────────────────────────────────

    private async Task<List<OhlcvCandle>> GetOrFetchAllAsync(string interval, DateTime upTo, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(interval, out var cached))
                return cached;

            var candles = await LoadAndBackfillFromDatabaseAsync(interval, upTo, ct);
            _cache[interval] = candles;
            return candles;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<OhlcvCandle>> LoadAndBackfillFromDatabaseAsync(string interval, DateTime upTo, CancellationToken ct)
    {
        var dbSymbol = ToDatabaseSymbol(interval);
        var intervalDuration = ToIntervalDuration(interval);

        using var scope = _scopeFactory.CreateScope();
        var stockCandleRepository = scope.ServiceProvider.GetRequiredService<IStockCandleRepository>();

        var dbCandles = await stockCandleRepository.GetStockCandlesUpToAsync(dbSymbol, DateTime.UtcNow.AddYears(1), ct);
        var allCandles = dbCandles
            .Select(c => new OhlcvCandle(
                OpenTime: DateTime.SpecifyKind(c.Timestamp, DateTimeKind.Utc).Subtract(intervalDuration),
                Open: (double)c.OpenPrice,
                High: (double)c.HighPrice,
                Low: (double)c.LowPrice,
                Close: (double)c.ClosePrice,
                Volume: c.Volume,
                CloseTime: DateTime.SpecifyKind(c.Timestamp, DateTimeKind.Utc)))
            .GroupBy(c => c.CloseTime)
            .Select(g => g.Last())
            .OrderBy(c => c.CloseTime)
            .ToList();

        // If DB history starts in Binance era, backfill the pre-Binance gap once from Bitstamp.
        if (TryGetBitstampStepSeconds(interval, out _)
            && allCandles.Count > 0)
        {
            var earliestClose = allCandles[0].CloseTime;
            var earliestOpen = earliestClose.Subtract(intervalDuration);
            var backfillEndExclusive = earliestClose.AddMilliseconds(1);
            if (earliestOpen > BitstampStartUtc)
            {
                try
                {
                    var preBinanceCandles = await FetchCandlesFromBitstampAsync(interval, BitstampStartUtc, backfillEndExclusive, ct);
                    if (preBinanceCandles.Count > 0)
                    {
                        var backfillToPersist = preBinanceCandles.Select(c => new StockCandle
                        {
                            Symbol = dbSymbol,
                            OpenPrice = (decimal)c.Open,
                            HighPrice = (decimal)c.High,
                            LowPrice = (decimal)c.Low,
                            ClosePrice = (decimal)c.Close,
                            Volume = checked((long)Math.Round(c.Volume, MidpointRounding.AwayFromZero)),
                            Timestamp = c.CloseTime
                        }).ToList();

                        await stockCandleRepository.SaveAsync(backfillToPersist, ct);
                        allCandles.InsertRange(0, preBinanceCandles);
                    }
                }
                catch (Exception ex)
                {
                    // Fail-open: keep existing DB-only coverage when Bitstamp cannot be reached.
                    Console.WriteLine($"[BinanceKlineService] Pre-Binance backfill failed for '{interval}': {ex.Message}");
                }
            }
        }

        var latestDbTimestamp = await stockCandleRepository.GetLatestTimestampForSymbolAsync(dbSymbol, ct);
        var fetchStart = latestDbTimestamp.HasValue
            ? DateTime.SpecifyKind(latestDbTimestamp.Value, DateTimeKind.Utc).AddMilliseconds(1)
            : GetSeedStart(upTo, intervalDuration, interval);

        var fetchedCandles = await FetchCandlesWithFallbackAsync(interval, fetchStart, ct);
        if (fetchedCandles.Count > 0)
        {
            var toPersist = fetchedCandles.Select(c => new StockCandle
            {
                Symbol = dbSymbol,
                OpenPrice = (decimal)c.Open,
                HighPrice = (decimal)c.High,
                LowPrice = (decimal)c.Low,
                ClosePrice = (decimal)c.Close,
                Volume = checked((long)Math.Round(c.Volume, MidpointRounding.AwayFromZero)),
                Timestamp = c.CloseTime
            }).ToList();

            await stockCandleRepository.SaveAsync(toPersist, ct);
            allCandles.AddRange(fetchedCandles);
        }

        var merged = allCandles
            .GroupBy(c => c.CloseTime)
            .Select(g => g.Last())
            .OrderBy(c => c.CloseTime)
            .ToList();

        Console.WriteLine($"[BinanceKlineService] Loaded {merged.Count} candles for '{interval}' (DB + delta fetch)");
        return merged;
    }

    private async Task<List<OhlcvCandle>> FetchCandlesWithFallbackAsync(string interval, DateTime startFromUtc, CancellationToken ct)
    {
        var normalizedStart = DateTime.SpecifyKind(startFromUtc, DateTimeKind.Utc);
        var merged = new List<OhlcvCandle>(4096);

        if (TryGetBitstampStepSeconds(interval, out _)
            && normalizedStart < BinanceStartUtc)
        {
            var bitstampStart = normalizedStart < BitstampStartUtc ? BitstampStartUtc : normalizedStart;
            if (bitstampStart < BinanceStartUtc)
            {
                try
                {
                    var bitstampCandles = await FetchCandlesFromBitstampAsync(interval, bitstampStart, BinanceStartUtc, ct);
                    merged.AddRange(bitstampCandles);
                }
                catch (Exception ex)
                {
                    // Fail-open: if Bitstamp is unavailable we still fetch Binance era candles.
                    Console.WriteLine($"[BinanceKlineService] Bitstamp fallback failed for '{interval}': {ex.Message}");
                }
            }
        }

        var binanceStart = normalizedStart < BinanceStartUtc ? BinanceStartUtc : normalizedStart;
        var binanceCandles = await FetchCandlesFromBinanceAsync(interval, binanceStart, ct);
        merged.AddRange(binanceCandles);

        return merged
            .GroupBy(c => c.CloseTime)
            .Select(g => g.Last())
            .OrderBy(c => c.CloseTime)
            .ToList();
    }

    private async Task<List<OhlcvCandle>> FetchCandlesFromBitstampAsync(
        string interval,
        DateTime startFromUtc,
        DateTime endExclusiveUtc,
        CancellationToken ct)
    {
        if (interval == "1w")
        {
            var daily = await FetchCandlesFromBitstampAsync("1d", startFromUtc, endExclusiveUtc, ct);
            var weekly = AggregateDailyToWeekly(daily, startFromUtc, endExclusiveUtc);
            Console.WriteLine($"[BinanceKlineService] Aggregated {weekly.Count} weekly fallback candles from Bitstamp daily data");
            return weekly;
        }

        if (!TryGetBitstampStepSeconds(interval, out var stepSeconds))
        {
            return [];
        }

        var intervalDuration = ToIntervalDuration(interval);
        var result = new List<OhlcvCandle>(4096);
        const int limit = 1000;

        var start = DateTime.SpecifyKind(startFromUtc, DateTimeKind.Utc);
        var endExclusive = DateTime.SpecifyKind(endExclusiveUtc, DateTimeKind.Utc);
        var startSeconds = new DateTimeOffset(start).ToUnixTimeSeconds();
        var endSeconds = new DateTimeOffset(endExclusive).ToUnixTimeSeconds();
        var cursorEnd = endSeconds;

        while (cursorEnd > startSeconds)
        {
            var url = $"https://www.bitstamp.net/api/v2/ohlc/btcusd/?step={stepSeconds}&limit={limit}&start={startSeconds}&end={cursorEnd}";
            using var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("ohlc", out var rows)
                || rows.ValueKind != JsonValueKind.Array
                || rows.GetArrayLength() == 0)
            {
                break;
            }

            var pageCount = 0;
            long minTimestamp = long.MaxValue;
            long maxTimestamp = long.MinValue;

            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("timestamp", out var tsElement)
                    || !long.TryParse(tsElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var openTs))
                {
                    continue;
                }

                var openTime = DateTimeOffset.FromUnixTimeSeconds(openTs).UtcDateTime;
                if (openTime < start || openTime >= endExclusive)
                {
                    continue;
                }

                if (!TryParseStringDouble(row, "open", out var open)
                    || !TryParseStringDouble(row, "high", out var high)
                    || !TryParseStringDouble(row, "low", out var low)
                    || !TryParseStringDouble(row, "close", out var close)
                    || !TryParseStringDouble(row, "volume", out var volume))
                {
                    continue;
                }

                var closeTime = openTime.Add(intervalDuration).AddMilliseconds(-1);
                result.Add(new OhlcvCandle(
                    OpenTime: openTime,
                    Open: open,
                    High: high,
                    Low: low,
                    Close: close,
                    Volume: volume,
                    CloseTime: closeTime));

                pageCount++;
                if (openTs < minTimestamp)
                {
                    minTimestamp = openTs;
                }
                if (openTs > maxTimestamp)
                {
                    maxTimestamp = openTs;
                }
            }

            if (pageCount == 0 || minTimestamp == long.MaxValue || maxTimestamp == long.MinValue)
            {
                break;
            }

            // Bitstamp returns the latest 'limit' candles within [start,end].
            // Move 'end' backwards to walk the full history window.
            if (minTimestamp <= startSeconds)
            {
                break;
            }

            var nextEnd = minTimestamp - stepSeconds;
            if (nextEnd >= cursorEnd)
            {
                break;
            }

            cursorEnd = nextEnd;
        }

        Console.WriteLine($"[BinanceKlineService] Fetched {result.Count} Bitstamp fallback candles for interval '{interval}'");
        return result;
    }

    private static List<OhlcvCandle> AggregateDailyToWeekly(
        IReadOnlyList<OhlcvCandle> dailyCandles,
        DateTime startFromUtc,
        DateTime endExclusiveUtc)
    {
        if (dailyCandles.Count == 0)
        {
            return [];
        }

        var start = DateTime.SpecifyKind(startFromUtc, DateTimeKind.Utc);
        var endExclusive = DateTime.SpecifyKind(endExclusiveUtc, DateTimeKind.Utc);

        return dailyCandles
            .OrderBy(c => c.OpenTime)
            .GroupBy(c => GetWeekStartMondayUtc(c.OpenTime))
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.OpenTime).ToList();
                var weekStart = g.Key;
                var weekClose = weekStart.AddDays(7).AddMilliseconds(-1);
                return new OhlcvCandle(
                    OpenTime: weekStart,
                    Open: ordered[0].Open,
                    High: ordered.Max(x => x.High),
                    Low: ordered.Min(x => x.Low),
                    Close: ordered[^1].Close,
                    Volume: ordered.Sum(x => x.Volume),
                    CloseTime: weekClose);
            })
            .Where(c => c.CloseTime >= start && c.OpenTime < endExclusive)
            .OrderBy(c => c.CloseTime)
            .ToList();
    }

    private static DateTime GetWeekStartMondayUtc(DateTime dateTimeUtc)
    {
        var d = DateTime.SpecifyKind(dateTimeUtc, DateTimeKind.Utc).Date;
        var offset = ((int)d.DayOfWeek + 6) % 7;
        return d.AddDays(-offset);
    }

    private async Task<List<OhlcvCandle>> FetchCandlesFromBinanceAsync(string interval, DateTime startFromUtc, CancellationToken ct)
    {
        var result = new List<OhlcvCandle>(4096);
        long startTimeMs = new DateTimeOffset(startFromUtc).ToUnixTimeMilliseconds();
        const int limit = 1000;

        while (true)
        {
            var url = $"/api/v3/klines?symbol=BTCUSDT&interval={interval}&startTime={startTimeMs}&limit={limit}";
            using var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var rows = JsonSerializer.Deserialize<JsonElement[][]>(json);
            if (rows is null || rows.Length == 0)
                break;

            foreach (var row in rows)
            {
                result.Add(new OhlcvCandle(
                    OpenTime: DateTimeOffset.FromUnixTimeMilliseconds(row[0].GetInt64()).UtcDateTime,
                    Open:     ParseDouble(row[1]),
                    High:     ParseDouble(row[2]),
                    Low:      ParseDouble(row[3]),
                    Close:    ParseDouble(row[4]),
                    Volume:   ParseDouble(row[5]),
                    CloseTime: DateTimeOffset.FromUnixTimeMilliseconds(row[6].GetInt64()).UtcDateTime
                ));
            }

            if (rows.Length < limit) break;

            startTimeMs = rows[^1][6].GetInt64() + 1; // next page starts after last close time
        }

        Console.WriteLine($"[BinanceKlineService] Fetched {result.Count} delta candles for interval '{interval}'");
        return result;
    }

    private static double ParseDouble(JsonElement element)
        => double.Parse(element.GetString() ?? "NaN", CultureInfo.InvariantCulture);

    private static bool TryParseStringDouble(JsonElement row, string propertyName, out double value)
    {
        value = default;
        if (!row.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        var raw = element.GetString();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetBitstampStepSeconds(string interval, out int stepSeconds)
    {
        stepSeconds = interval switch
        {
            "1h" => 3600,
            "1d" => 86400,
            "1w" => 604800,
            _ => 0
        };

        return stepSeconds > 0;
    }

    private static string ToDatabaseSymbol(string interval) => interval switch
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

    private static TimeSpan ToIntervalDuration(string interval) => interval switch
    {
        "5m" => TimeSpan.FromMinutes(5),
        "15m" => TimeSpan.FromMinutes(15),
        "30m" => TimeSpan.FromMinutes(30),
        "1h" => TimeSpan.FromHours(1),
        "2h" => TimeSpan.FromHours(2),
        "4h" => TimeSpan.FromHours(4),
        "12h" => TimeSpan.FromHours(12),
        "1d" => TimeSpan.FromDays(1),
        "1w" => TimeSpan.FromDays(7),
        _ => throw new ArgumentException($"Unsupported interval: {interval}", nameof(interval))
    };

    private static int BinarySearchLastCloseTimeLe(List<OhlcvCandle> candles, DateTime upTo)
    {
        int lo = 0, hi = candles.Count - 1, result = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (candles[mid].CloseTime <= upTo)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return result;
    }

    private static int BinarySearchFirstCloseTimeGe(List<OhlcvCandle> candles, DateTime from)
    {
        int lo = 0, hi = candles.Count - 1, result = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (candles[mid].CloseTime >= from)
            {
                result = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }
        return result;
    }

    private static DateTime GetSeedStart(DateTime upTo, TimeSpan intervalDuration, string interval)
    {
        var seedStart = DateTime.SpecifyKind(upTo, DateTimeKind.Utc) - TimeSpan.FromTicks(intervalDuration.Ticks * SeedLookbackCandles);
        var dataStartUtc = TryGetBitstampStepSeconds(interval, out _) ? BitstampStartUtc : BinanceStartUtc;
        return seedStart > dataStartUtc ? seedStart : dataStartUtc;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _lock.Dispose();
    }
}
