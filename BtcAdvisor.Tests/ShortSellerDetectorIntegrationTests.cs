using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class ShortSellerDetectorIntegrationTests
{
    private class TestDateTimeProvider(DateTime start) : IDateTimeProvider
    {
        private DateTime _utcNow = start;

        public DateTime UtcNow => _utcNow;

        public DateTime GetNow ()
        {
            return _utcNow.ToLocalTime ();
        }

        public DateTime GetUtcNow ()
        {
            return _utcNow;
        }

        public void SetUtcNow (DateTime t) => _utcNow = t;
    }

    private class FakeStockCandleRepository (IEnumerable<StockCandle> candles) : IStockCandleRepository
    {
        private readonly List<StockCandle> _candles = [..candles];

        public Task<DateTime> GetMaxTimestamp(string symbol, DateTime dateTimeNow, CancellationToken cancellationToken)
        {
            return Task.FromResult(dateTimeNow);
        }

        public Task<StockCandle?> GetStockCandle(string symbol, DateTime dateTime, TimeSpan timeSpan, CancellationToken cancellationToken)
        {
            var fromUtc = dateTime; // START des Fensters
            var toUtc = dateTime.Add(timeSpan); // ENDE (exklusiv)

            var slice = _candles
                .Where(c => c.Symbol == symbol && c.Timestamp >= fromUtc && c.Timestamp < toUtc)
                .OrderBy(c => c.Timestamp)
                .ToList();

            if (slice.Count == 0)
                return Task.FromResult<StockCandle?>(null);

            // Für 15m ok, für 60m (oder größer) aggregieren wir:
            var agg = new StockCandle
            {
                Symbol = symbol,
                Timestamp = fromUtc,
                OpenPrice = slice.First().OpenPrice,
                HighPrice = slice.Max(x => x.HighPrice),
                LowPrice = slice.Min(x => x.LowPrice),
                ClosePrice = slice.Last().ClosePrice,
                Volume = slice.Sum(x => x.Volume)
            };
            return Task.FromResult<StockCandle?>(agg);
        }

        public Task SaveAsync (IReadOnlyCollection<StockCandle> stockCandle, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        Task<DateTime?> IStockCandleRepository.GetMaxTimestamp(string symbol, DateTime dateTimeNow, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<StockCandle>> GetStockCandlesUpToAsync(string symbol, DateTime endDate, CancellationToken cancellationToken)
        {
            var result = _candles
                .Where(c => c.Symbol == symbol && c.Timestamp <= endDate)
                .OrderBy(c => c.Timestamp)
                .ToList();
            return Task.FromResult<IReadOnlyList<StockCandle>>(result.AsReadOnly());
        }

        public Task<IReadOnlyList<StockCandle>> GetStockCandlesInRangeAsync(string symbol, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
        {
            var result = _candles
                .Where(c => c.Symbol == symbol && c.Timestamp >= startDate && c.Timestamp <= endDate)
                .OrderBy(c => c.Timestamp)
                .ToList();
            return Task.FromResult<IReadOnlyList<StockCandle>>(result.AsReadOnly());
        }

        public Task<DateTime?> GetLatestTimestampForSymbolAsync(string symbol, CancellationToken cancellationToken)
        {
            var latest = _candles
                .Where(c => c.Symbol == symbol)
                .OrderByDescending(c => c.Timestamp)
                .Select(c => (DateTime?)c.Timestamp)
                .FirstOrDefault();

            return Task.FromResult(latest);
        }

        public Task<IReadOnlyCollection<DateTime>> GetDistinctTimestampsAsync(string symbol, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
        {
            IReadOnlyCollection<DateTime> result = _candles
                .Where(c => c.Symbol == symbol && c.Timestamp >= startDate && c.Timestamp <= endDate)
                .Select(c => c.Timestamp)
                .Distinct()
                .OrderBy(c => c)
                .ToList()
                .AsReadOnly();

            return Task.FromResult(result);
        }
    }

    private class FakeBitcoinStatisticsService (IEnumerable<StockCandle> candles) : IBitcoinStatisticsService
    {
        private readonly List<StockCandle> _candles = [..candles];
        public string DebugLog { get; private set; } = "";

        private static TimeSpan ParseTimeSpan(string timeSpan)
        {
            if (string.IsNullOrWhiteSpace(timeSpan)) return TimeSpan.FromMinutes(15);
            var s = timeSpan.Trim().ToLowerInvariant();
            if (s.EndsWith('m') && double.TryParse(s[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
            {
                return TimeSpan.FromMinutes(m);
            }
            if (s.EndsWith('h') && double.TryParse(s[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            {
                return TimeSpan.FromHours(h);
            }

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var plainMinutes))
            {
                return TimeSpan.FromMinutes(plainMinutes);
            }
            return TimeSpan.FromMinutes(15);
        }

        public Task<BitcoinStatistics> GetStatistics(DateTime dateTime, bool IsOszillator, string timeSpan, CancellationToken cancellationToken)
        {
            var span    = ParseTimeSpan(timeSpan);
            var fromUtc = dateTime - span;     // [dateTime - span, dateTime)
            var toUtc   = dateTime;

            var slice = _candles
                .Where(c => c.Timestamp >= fromUtc && c.Timestamp < toUtc)
                .OrderBy(c => c.Timestamp)
                .ToList();

            // Debug-Infos sammeln
            var firstTs = slice.Count > 0 ? slice.First().Timestamp.ToString("o") : "—";
            var lastTs  = slice.Count > 0 ? slice.Last().Timestamp.ToString("o")  : "—";
            DebugLog = $"BTC window [{fromUtc:o} .. {toUtc:o})  count={slice.Count}  first={firstTs}  last={lastTs}";

            if (slice.Count == 0)
            {
                return Task.FromResult(new BitcoinStatistics
                {
                    DateTime = dateTime,
                    Price = 0.0,
                    TimeRangeStatistics =
                    [
                        new()
                        {
                            TimespanHours = span.TotalHours,
                            IndicatorSellCount = 0,
                            IndicatorBuyCount  = 0
                        }
                    ]
                });
            }

            var price = (double)slice.Last().ClosePrice; // Repräsentativpreis: letzter Close im Fenster

            return Task.FromResult(new BitcoinStatistics
            {
                DateTime = dateTime,
                Price = price,
                TimeRangeStatistics =
                [
                    new()
                    {
                        TimespanHours = span.TotalHours,
                        IndicatorSellCount = 1,
                        IndicatorBuyCount  = 1
                    }
                ]
            });
        }
    }

    private class FakeShortSellerStateRepository : IShortSellerStateRepository
    {
        private readonly List < (ShortSellerPhase Phase, DateTime TimeStamp) > _phases = [];

        public Task SaveAsync (ShortSellerPhase shortSellerPhase, DateTime dateTime, CancellationToken cancellationToken)
        {
            _phases.Add (new PhaseTime (shortSellerPhase, dateTime));
            return Task.CompletedTask;
        }

        public Task<ShortSellerPhase> GetLatestShortSellerPhase (CancellationToken cancellationToken)
        {
            return Task.FromResult (_phases.LastOrDefault ().Phase);
        }

        public Task < (ShortSellerPhase Phase, DateTime TimeStamp) > GetLatestShortSellerPhaseTime (CancellationToken cancellationToken)
        {
            return Task.FromResult (_phases.LastOrDefault ());
        }

        public Task<List<(string TradeAdvice, decimal Price, DateTime TimeStamp)>> GetHistoryAsync(int take, CancellationToken ct)
        {
            return Task.FromResult (_phases.OrderByDescending(x => x.TimeStamp).Take(3).Select(x => (x.Phase.ToString(), 0m, x.TimeStamp)).ToList());
        }

        public IReadOnlyList < (ShortSellerPhase Phase, DateTime TimeStamp) > Saved => _phases;
    }

    private static List<StockCandle> LoadMstrCandles (string path)
    {
        var json = File.ReadAllText (path);
        var items = JsonSerializer.Deserialize<List<MstrJson>> (json) !;

        return [..items.Select (x => new StockCandle
        {
            Symbol = "MSTR",
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds (x.openTime).UtcDateTime,
            OpenPrice = (decimal) x.o,
            HighPrice = (decimal) x.h,
            LowPrice = (decimal) x.l,
            ClosePrice = (decimal) x.c,
            Volume = (long) x.v
        })];
    }

    private static List<StockCandle> LoadBtcCandles(string path)
    {
        var json = File.ReadAllText(path);
        var items = JsonSerializer.Deserialize<List<BtcJson>>(json)!;

        static decimal D(string s) => decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        return [.. items.Select(x => new StockCandle
        {
            Timestamp  = DateTimeOffset.FromUnixTimeMilliseconds(x.openTime).UtcDateTime,
            OpenPrice  = D(x.open),
            HighPrice  = D(x.high),
            LowPrice   = D(x.low),
            ClosePrice = D(x.close),
            Volume     = (long)D(x.volume)
        })];
    }

    // DTOs für JSON-Parsing
#pragma warning disable IDE1006 // Naming Styles
    private record MstrJson (long t, double o, double h, double l, double c, double v, long openTime);
    private record BtcJson (long openTime, string open, string high, string low, string close, string volume);
#pragma warning restore IDE1006 // Naming Styles

    [Test]
    public async Task DetectsShortSeller_On2025_07_18 ()
    {
        // Arrange
        var mstrCandles = LoadMstrCandles ("testdata/mstr_2025-07-18_13-30_utc.json");
        var btcCandles = LoadBtcCandles ("testdata/btcusdt_2025-07-18_13-30_utc.json");
        var clock = new TestDateTimeProvider (new DateTime (2025, 7, 18, 13, 30, 0, DateTimeKind.Utc));
        var stockRepo = new FakeStockCandleRepository (mstrCandles);
        var btcSvc = new FakeBitcoinStatisticsService (btcCandles);
        var stateRepo = new FakeShortSellerStateRepository ();

        var services = new ServiceCollection ();
        services.AddSingleton<IStockCandleRepository> (stockRepo);
        services.AddSingleton<IBitcoinStatisticsService> (btcSvc);
        services.AddSingleton<IShortSellerStateRepository> (stateRepo);
        services.AddSingleton<IDateTimeProvider> (clock);

        var sp = services.BuildServiceProvider ();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory> ();

        var sut = new ShortSellerDetectorService (scopeFactory, clock, NullLogger<ShortSellerDetectorService>.Instance);

        var minBtcTs = btcCandles.Min(c => c.Timestamp);
        var maxBtcTs = btcCandles.Max(c => c.Timestamp);
        TestContext.Out.WriteLine($"BTC fixture range: {minBtcTs:o} .. {maxBtcTs:o}");

        // Act: jede 15m-Kerze simulieren
        for (var i = 0; i < 8; i++)
        {
            await sut.DetectAndPersistAsync(CancellationToken.None);

            TestContext.Out.WriteLine($"--- Step {i+1} @ {clock.UtcNow:o} ---");
            TestContext.Out.WriteLine(sut.DebugString);

            // BTC-Fensterdiagnose
            TestContext.Out.WriteLine(btcSvc.DebugLog);
            TestContext.Out.WriteLine();

            clock.SetUtcNow(clock.UtcNow.AddMinutes(15));
        }

        // Assert: Übergänge loggen
        TestContext.Out.WriteLine ("Detected phases:");
        foreach (var (Phase, TimeStamp) in stateRepo.Saved)
        {
            TestContext.Out.WriteLine ($"Phase={Phase} at {TimeStamp}");
        }

        Assert.That (stateRepo.Saved, Is.Not.Empty, "No phase transitions were detected");

        Assert.That (stateRepo.Saved.Any (p => p.Phase == ShortSellerPhase.IsSelling),
            "Expected ShortSellerPhase.IsSelling during the test window");
    }

    [Test]
    public async Task DetectsShortSeller_On2025_05_12 ()
    {
        // Arrange
        var mstrCandles = LoadMstrCandles ("testdata/mstr_2025-05-12_11-00_utc.json");
        var btcCandles = LoadBtcCandles ("testdata/btcusdt_2025-05-12_11-00_utc.json");
        var clock = new TestDateTimeProvider (new DateTime (2025, 5, 12, 11, 00, 0, DateTimeKind.Utc));
        var stockRepo = new FakeStockCandleRepository (mstrCandles);
        var btcSvc = new FakeBitcoinStatisticsService (btcCandles);
        var stateRepo = new FakeShortSellerStateRepository ();

        var services = new ServiceCollection ();
        services.AddSingleton<IStockCandleRepository> (stockRepo);
        services.AddSingleton<IBitcoinStatisticsService> (btcSvc);
        services.AddSingleton<IShortSellerStateRepository> (stateRepo);
        services.AddSingleton<IDateTimeProvider> (clock);

        var sp = services.BuildServiceProvider ();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory> ();

        var sut = new ShortSellerDetectorService (scopeFactory, clock, NullLogger<ShortSellerDetectorService>.Instance);

        var minBtcTs = btcCandles.Min(c => c.Timestamp);
        var maxBtcTs = btcCandles.Max(c => c.Timestamp);
        TestContext.Out.WriteLine($"BTC fixture range: {minBtcTs:o} .. {maxBtcTs:o}");

        // Act: jede 15m-Kerze simulieren
        for (var i = 0; i < 20; i++)
        {
            await sut.DetectAndPersistAsync(CancellationToken.None);

            TestContext.Out.WriteLine($"--- Step {i+1} @ {clock.UtcNow:o} ---");
            TestContext.Out.WriteLine(sut.DebugString);

            // BTC-Fensterdiagnose
            TestContext.Out.WriteLine(btcSvc.DebugLog);
            TestContext.Out.WriteLine();

            clock.SetUtcNow(clock.UtcNow.AddMinutes(15));
        }

        // Assert: Übergänge loggen
        TestContext.Out.WriteLine ("Detected phases:");
        foreach (var (Phase, TimeStamp) in stateRepo.Saved)
        {
            TestContext.Out.WriteLine ($"Phase={Phase} at {TimeStamp}");
        }

        Assert.That (stateRepo.Saved, Is.Not.Empty, "No phase transitions were detected");

        Assert.That (stateRepo.Saved.Any (p => p.Phase == ShortSellerPhase.IsSelling),
            "Expected ShortSellerPhase.IsSelling during the test window");
    }
}

internal record struct PhaseTime (ShortSellerPhase ShortSellerPhase, DateTime DateTime)
{
    public static implicit operator (ShortSellerPhase shortSellerPhase, DateTime dateTime) (PhaseTime value) => (value.ShortSellerPhase, value.DateTime);

    public static implicit operator PhaseTime ((ShortSellerPhase shortSellerPhase, DateTime dateTime) value) => new (value.shortSellerPhase, value.dateTime);
}
