using Moq;
using BtcAdvisor.Repository;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BtcAdvisor.Tests;

[TestFixture]
public class ShortSellerDetectorServiceTests
{
    private Mock<IServiceScopeFactory> _mockServiceScopeFactory;
    private Mock<IServiceScope> _mockServiceScope;
    private Mock<IServiceProvider> _mockServiceProvider;
    private Mock<IStockCandleRepository> _mockStockCandleRepository;
    private Mock<IBitcoinStatisticsService> _mockBitcoinStatisticsService;
    private Mock<IShortSellerStateRepository> _mockShortSellerStateRepository;
    private Mock<IDateTimeProvider> _mockDateTimeProvider;
    private ShortSellerDetectorService _service;

    [SetUp]
    public void Setup()
    {
        _mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
        _mockServiceScope = new Mock<IServiceScope>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockStockCandleRepository = new Mock<IStockCandleRepository>();
        _mockBitcoinStatisticsService = new Mock<IBitcoinStatisticsService>();
        _mockShortSellerStateRepository = new Mock<IShortSellerStateRepository>();
        _mockDateTimeProvider = new Mock<IDateTimeProvider>();

        _mockServiceScopeFactory.Setup(x => x.CreateScope()).Returns(_mockServiceScope.Object);
        _mockServiceScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IStockCandleRepository))).Returns(_mockStockCandleRepository.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IBitcoinStatisticsService))).Returns(_mockBitcoinStatisticsService.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IShortSellerStateRepository))).Returns(_mockShortSellerStateRepository.Object);

        _service = new ShortSellerDetectorService(_mockServiceScopeFactory.Object, _mockDateTimeProvider.Object);
    }

    [TestCase("up", "up", false, ShortSellerPhase.None, ShortSellerPhase.Unchanged)]
    [TestCase("down", "down", false, ShortSellerPhase.None, ShortSellerPhase.Unchanged)]
    [TestCase("down", "up", true, ShortSellerPhase.None, ShortSellerPhase.IsSelling)]
    [TestCase("up", "down", true, ShortSellerPhase.None, ShortSellerPhase.IsBuying)]
    [TestCase("up", "up", true, ShortSellerPhase.Unchanged, ShortSellerPhase.Unchanged)]
    [TestCase("up", "up", false, ShortSellerPhase.Unchanged, ShortSellerPhase.Unchanged)]
    [TestCase("down", "down", false, ShortSellerPhase.Unchanged, ShortSellerPhase.Unchanged)]
    [TestCase("down", "up", true, ShortSellerPhase.Unchanged, ShortSellerPhase.IsSelling)]
    [TestCase("up", "down", true, ShortSellerPhase.Unchanged, ShortSellerPhase.IsBuying)]
    [TestCase("up", "up", true, ShortSellerPhase.IsSelling, ShortSellerPhase.Unchanged)]
    [TestCase("up", "up", false, ShortSellerPhase.IsSelling, ShortSellerPhase.Unchanged)]
    [TestCase("down", "down", false, ShortSellerPhase.IsSelling, ShortSellerPhase.Unchanged)]
    [TestCase("down", "up", true, ShortSellerPhase.IsSelling, ShortSellerPhase.Unchanged)]
    [TestCase("up", "down", true, ShortSellerPhase.IsSelling, ShortSellerPhase.IsBuying)]
    [TestCase("up", "up", true, ShortSellerPhase.IsBuying, ShortSellerPhase.Unchanged)]
    [TestCase("up", "up", true, ShortSellerPhase.IsBuying, ShortSellerPhase.Unchanged)]
    [TestCase("up", "up", false, ShortSellerPhase.IsBuying, ShortSellerPhase.Unchanged)]
    [TestCase("down", "down", false, ShortSellerPhase.IsBuying, ShortSellerPhase.Unchanged)]
    [TestCase("down", "up", true, ShortSellerPhase.IsBuying, ShortSellerPhase.IsSelling)]
    [TestCase("up", "down", true, ShortSellerPhase.IsBuying, ShortSellerPhase.Unchanged)]
    [TestCase("up", "up", true, ShortSellerPhase.IsBuying, ShortSellerPhase.Unchanged)]
    public async Task GetShortSellerPhase_ShouldReturnExpectedPhase(
        string mstrTrend, string btcTrend, bool moreVolatile, ShortSellerPhase previousPhase, ShortSellerPhase expectedPhase)
    {
        // Arrange
        SetupCandles(mstrTrend, 100d, 95d, btcTrend, 10000d, 9900d, moreVolatile);

        _mockShortSellerStateRepository.Setup(x => x.GetLatestShortSellerPhase(It.IsAny<CancellationToken>()))
            .ReturnsAsync(previousPhase);

        // Act
        var result = await _service.GetShortSellerPhase(CancellationToken.None);

        // Assert
        Assert.That(result, Is.EqualTo(expectedPhase), () => _service.DebugString);
    }

    private void SetupCandles(string mstrTrend, double mstrRecentMid, double mstrBeforeMid,
                            string btcTrend,  double btcRecentPrice, double btcBeforePrice,
                            bool moreVolatile)
    {
        // Zeitachsen wie im Service
        var now = new DateTime(2025, 7, 18, 15, 30, 0, DateTimeKind.Utc);
        _mockDateTimeProvider.Setup(d => d.GetUtcNow()).Returns(now);

        var win = TimeSpan.FromMinutes(60);
        var startBefore = now - win - win; // [now-120, now-60)
        var startRecent = now - win;       // [now-60,  now)

        // Schmale Range für "ruhige" Baseline (±0.3 %)
        static (decimal hi, decimal lo) BandNarrow(double mid)
        {
            var m = (decimal)mid;
            var hi = m * 1.003m;
            var lo = m * 0.997m;
            return (hi, lo);
        }

        var (hiPrev, loPrev) = BandNarrow(mstrBeforeMid);
        decimal hiNow = hiPrev, loNow = loPrev;

        // Ruhige Gleichlauf-Szenarien: Mid so verschieben, dass das Vorzeichen passt,
        // aber KEIN Break entsteht (|UpBreak|,|DownBreak| < 1.5%)
        if (!moreVolatile && mstrTrend == "down" && btcTrend == "down")
        {
            // BTC leicht runter (~ -0.8 %) -> innerhalb Guard, aber > MinBtcMove
            btcRecentPrice = btcBeforePrice * 0.992; // -0.8 %

            // Vorperiodenband (schmal)
            var midPrev = (hiPrev + loPrev) / 2m;

            // MSTR mid ungefähr gleich dem BTC-Change (kein Underperf-Trigger)
            var midNow = midPrev * 0.992m;

            // Sehr schmales Band um midNow -> keine Breaks
            var band = 0.001m; // ±0.1 %
            hiNow = midNow * (1 + band);
            loNow = midNow * (1 - band);

            // Sicherheits-CLAMPS: Breaks zuverlässig verhindern
            // UpBreak = hiNow/loPrev - 1  < +1.5 %
            if ((double)(hiNow / loPrev - 1m) >= 0.015) hiNow = loPrev * 1.014m;

            // DownBreak = loNow/hiPrev - 1 > -1.5 %
            if ((double)(loNow / hiPrev - 1m) <= -0.015) loNow = hiPrev * 0.986m;

            // Falls Numerik kippt: hi >= lo garantieren
            if (hiNow <= loNow) hiNow = loNow * 1.001m;
        }
        if (!moreVolatile)
        {
            if (mstrTrend == "up")
            {
                // mid_now minimal > mid_prev, aber hi_now darf NICHT zu stark über lo_prev steigen
                // => hi_now knapp unter loPrev*1.015, low_now leicht über loPrev
                var hiCap = loPrev * 1.014m; // UpBreak ~1.4% -> unter 1.5%
                hiNow = Math.Min(hiPrev * 1.001m, hiCap); // minimal höher, aber unter Cap
                loNow = Math.Max(loPrev * 1.001m, (hiNow + loPrev) / 2m - 0.1m);
                if (loNow > hiNow) loNow = hiNow * 0.999m;
            }
        }
        else
        {
            // Volatile Divergenz-Fälle so bauen, dass GENAU der gewünschte Break triggert
            if (mstrTrend == "down" && btcTrend == "up")
            {
                // IsSelling: LowerHigh + kräftiger DownBreak, BTC ruhig/leicht + (innerhalb Guard)
                hiNow = hiPrev * 0.992m;             // lowerHigh
                loNow = hiPrev * 0.960m;             // downBreak ≈ -4%  << -1.5%
                btcRecentPrice = btcBeforePrice * 1.008; // +0.8% (innerhalb ±1.1%)
            }
            else if (mstrTrend == "up" && btcTrend == "down")
            {
                // IsBuying: HigherLow + UpBreak, BTC ruhig/leicht - (innerhalb Guard)
                loNow = loPrev * 1.010m;             // higherLow
                hiNow = loPrev * 1.030m;             // upBreak ≈ +3%  >> +1.5%
                // Achtung: DownBreak muss AUS bleiben -> loNow nahe hiPrev (nicht weit darunter)
                if ((double)(loNow / hiPrev - 1m) <= -0.015) loNow = hiPrev * 0.995m;
                if (loNow > hiNow) hiNow = loNow * 1.002m;

                btcRecentPrice = btcBeforePrice * 0.993; // -0.7% (innerhalb ±1.1%)
            }
            else
            {
                // sonst „etwas mehr Bewegung“, aber weiterhin ohne Break
                hiNow = hiPrev * 1.002m;
                loNow = loPrev * 0.998m;

                // Breaks explizit clampen
                if ((double)(hiNow / loPrev - 1m) >= 0.015) hiNow = loPrev * 1.014m;
                if ((double)(loNow / hiPrev - 1m) <= -0.015) loNow = hiPrev * 0.986m;
            }
        }

        // MSTR-Repo-Mocks (60m-Fenster; Startzeiten sind die Fensterränder)
        _mockStockCandleRepository
            .Setup(r => r.GetStockCandle("MSTR", startRecent, win, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCandle {
                Symbol    = "MSTR",
                Timestamp = startRecent,
                HighPrice = hiNow,
                LowPrice  = loNow,
                OpenPrice = (hiNow + loNow) / 2,
                ClosePrice= (hiNow + loNow) / 2,
                Volume    = 1000
            });

        _mockStockCandleRepository
            .Setup(r => r.GetStockCandle("MSTR", startBefore, win, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockCandle {
                Symbol    = "MSTR",
                Timestamp = startBefore,
                HighPrice = hiPrev,
                LowPrice  = loPrev,
                OpenPrice = (hiPrev + loPrev) / 2,
                ClosePrice= (hiPrev + loPrev) / 2,
                Volume    = 1000
            });

        // BTC-Stats (60m Look-back)
        _mockBitcoinStatisticsService
            .Setup(s => s.GetStatistics(now, true, "1h", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BitcoinStatistics {
                DateTime = now, Price = btcRecentPrice,
                TimeRangeStatistics = [ new() { TimespanHours = 1, IndicatorBuyCount = 1, IndicatorSellCount = 1 } ]
            });

        _mockBitcoinStatisticsService
            .Setup(s => s.GetStatistics(now - win, true, "1h", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BitcoinStatistics {
                DateTime = now - win, Price = btcBeforePrice,
                TimeRangeStatistics = [ new() { TimespanHours = 1, IndicatorBuyCount = 1, IndicatorSellCount = 1 } ]
            });
    }

}
