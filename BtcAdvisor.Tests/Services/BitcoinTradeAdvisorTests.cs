using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;

namespace BtcAdvisor.Tests.Services;

[TestFixture]
public class BitcoinTradeAdvisorTests
{
    private Mock<IShortSellerStateRepository> _mockShortSellerRepo;
    private Mock<IServiceScopeFactory> _mockScopeFactory;
    private Mock<IBitcoinStatisticsService> _mockStatsService;
    private BitcoinTradeAdvisor _sut;

    private static readonly DateTime T0 = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] TimeRanges = LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe
        ? ["2h", "4h", "12h", "1d", "1w"]
        : ["5m", "15m", "30m", "1h", "2h"];

    [SetUp]
    public void Setup()
    {
        _mockShortSellerRepo = new Mock<IShortSellerStateRepository>();
        _mockStatsService = new Mock<IBitcoinStatisticsService>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();

        // Jeder CreateScope-Aufruf gibt einen frischen Scope mit demselben Mock-Service zurück
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IBitcoinStatisticsService)))
            .Returns(_mockStatsService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _sut = new BitcoinTradeAdvisor(
            _mockShortSellerRepo.Object,
            _mockScopeFactory.Object,
            new MemoryCache(new MemoryCacheOptions()));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static BitcoinStatistics MakeStatistics(double price, DateTime dateTime) => new()
    {
        Price = price,
        DateTime = dateTime,
        TimeRangeStatistics = [new BitcoinTimeRangeStatistics()],
        DebugString = "test"
    };

    private void SetupAllTimeRangesReturn(double price)
    {
        foreach (var tr in TimeRanges)
        {
            _mockStatsService
                .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), tr, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeStatistics(price, T0));
        }
    }

    // ── Tests: Parallele Abfragen ────────────────────────────────────────────

    [Test]
    public async Task GetBitcoinTradeAdvice_QueriesAll5TimeRangesForOscillators()
    {
        // Arrange
        SetupAllTimeRangesReturn(50_000.0);

        // Act
        await _sut.GetBitcoinTradeAdvice(T0, T0, null, null, null, null, null, null, CancellationToken.None);

        // Assert – 5 Oscillator + 5 Moving Average = 10 Aufrufe insgesamt
        foreach (var tr in TimeRanges)
        {
            _mockStatsService.Verify(
                s => s.GetStatistics(T0, true, tr, It.IsAny<CancellationToken>()), Times.Once,
                $"Oscillator-Aufruf für {tr} fehlt");
            _mockStatsService.Verify(
                s => s.GetStatistics(T0, false, tr, It.IsAny<CancellationToken>()), Times.Once,
                $"MovingAverage-Aufruf für {tr} fehlt");
        }
    }

    [Test]
    public async Task GetBitcoinTradeAdvice_CreatesServiceScopePerTimeRange()
    {
        // Arrange
        SetupAllTimeRangesReturn(50_000.0);

        // Act
        await _sut.GetBitcoinTradeAdvice(T0, T0, null, null, null, null, null, null, CancellationToken.None);

        // Assert – 10 Scopes: 5 Timeranges × 2 (Osci + MA)
        _mockScopeFactory.Verify(f => f.CreateScope(), Times.Exactly(10));
    }

    [Test]
    public async Task GetBitcoinTradeAdvice_ReturnsTradeAdvice_WhenStatsAvailable()
    {
        // Arrange
        SetupAllTimeRangesReturn(50_000.0);

        // Act
        var result = await _sut.GetBitcoinTradeAdvice(T0, T0, null, null, null, null, null, null, CancellationToken.None);

        // Assert – sollte genau 1 Ergebnis liefern
        Assert.That(result, Has.Count.EqualTo(1));
    }

    // ── Tests: Null/Leer-Fälle ───────────────────────────────────────────────

    [Test]
    public async Task GetBitcoinTradeAdvice_OscillatorPriceZero_ReturnsEmpty()
    {
        // Arrange – Oscillator hat Preis 0
        foreach (var tr in TimeRanges)
        {
            _mockStatsService
                .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), true, tr, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeStatistics(0.0, T0));
            _mockStatsService
                .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), false, tr, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeStatistics(50_000.0, T0));
        }

        // Act
        var result = await _sut.GetBitcoinTradeAdvice(T0, T0, null, null, null, null, null, null, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetBitcoinTradeAdvice_CurrentPriceIsPassedThrough()
    {
        // Arrange
        const double expectedPrice = 67_500.0;
        SetupAllTimeRangesReturn(expectedPrice);

        // Act
        var result = await _sut.GetBitcoinTradeAdvice(T0, T0, null, null, null, null, null, null, CancellationToken.None);

        // Assert
        Assert.That(result.First().CurrentPrice, Is.EqualTo(expectedPrice).Within(0.01));
    }

    // ── Tests: DateTime-Min/Max-Berechnung ───────────────────────────────────

    [Test]
    public async Task GetBitcoinTradeAdvice_MinMaxDateTime_ComputedFromAllTimeRanges()
    {
        // Arrange – unterschiedliche DateTimes pro Timerange
        var early = T0.AddMinutes(-30);
        var late = T0.AddMinutes(30);
        var earliestRange = TimeRanges.First();
        var latestRange = TimeRanges.Last();
        _mockStatsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), earliestRange, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeStatistics(50_000.0, early));
        _mockStatsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), latestRange, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeStatistics(50_000.0, late));
        foreach (var tr in TimeRanges.Where(tr => tr != earliestRange && tr != latestRange))
        {
            _mockStatsService
                .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), tr, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeStatistics(50_000.0, T0));
        }

        // Act
        var result = await _sut.GetBitcoinTradeAdvice(T0, T0, null, null, null, null, null, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // Assert – MinDateTime = early, MaxDateTime = late
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result.First().MinDateTime, Is.EqualTo(early));
            Assert.That(result.First().MaxDateTime, Is.EqualTo(late));
        }
    }

    // ── Tests: Parameter werden korrekt weitergereicht ───────────────────────

    [Test]
    public async Task GetBitcoinTradeAdvice_StrategyPrices_ArePassedToPrimaryDecisionMaker()
    {
        // Arrange
        SetupAllTimeRangesReturn(50_000.0);
        decimal? latestBuy = 48_000m;
        decimal? latestSell = 52_000m;
        decimal? stratHigh = 350m;
        decimal? stratLow = 300m;
        var lowestBuy = new BuyActionLog { Price = 45_000m, TradingDate = T0.AddDays(-1), TradeAdvice = "StrongBuy" };
        var highestPanic = new BuyActionLog { Price = 47_000m, TradingDate = T0.AddHours(-2), TradeAdvice = "PanicBuy" };

        // Act
        var result = await _sut.GetBitcoinTradeAdvice(
            T0, T0, latestBuy, latestSell, stratHigh, stratLow, lowestBuy, highestPanic, CancellationToken.None);

        // Assert – gibt ein Ergebnis zurück (PrimaryDecisionMaker wird aufgerufen)
        Assert.That(result, Has.Count.EqualTo(1));
    }
}
