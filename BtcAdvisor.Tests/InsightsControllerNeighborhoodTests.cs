using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using BtcAdvisor.Controllers;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;

namespace BtcAdvisor.Tests;

[TestFixture]
public class InsightsControllerNeighborhoodTests
{
    private Mock<IBitcoinStatisticsService> _statsService = null!;
    private Mock<IStockCandleRepository> _stockCandleRepository = null!;
    private Mock<IBuyActionLogRepository> _buyActionLogRepository = null!;
    private Mock<ISellActionLogRepository> _sellActionLogRepository = null!;
    private ApplicationDbContext _dbContext = null!;
    private InsightsController _sut = null!;

    [SetUp]
    public void Setup()
    {
        _statsService = new Mock<IBitcoinStatisticsService>(MockBehavior.Strict);
        _stockCandleRepository = new Mock<IStockCandleRepository>(MockBehavior.Strict);
        _buyActionLogRepository = new Mock<IBuyActionLogRepository>(MockBehavior.Strict);
        _sellActionLogRepository = new Mock<ISellActionLogRepository>(MockBehavior.Strict);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"insights-neighborhood-{Guid.NewGuid()}")
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _sut = new InsightsController(
            _statsService.Object,
            _dbContext,
            _stockCandleRepository.Object,
            _buyActionLogRepository.Object,
            _sellActionLogRepository.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [Test]
    public async Task GetTurningPointNeighborhood_WhenWindowInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetTurningPointNeighborhood(
            new DateTime(2023, 6, 14, 0, 0, 0, DateTimeKind.Utc),
            windowDays: 0,
            minTrendIndicators: 3,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetTurningPointNeighborhood_WhenDataAvailable_ReturnsEntriesAndConfluenceMetrics()
    {
        var center = new DateTime(2023, 6, 14, 23, 59, 59, DateTimeKind.Utc);
        var candles = BuildDailyCandles(center.AddDays(-50), 101, "BTC1D");

        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("BTC1D", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        _statsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime ts, bool isOscillator, string timeRange, CancellationToken _) =>
                BuildStatistics(ts, isOscillator, timeRange, center));

        var result = await _sut.GetTurningPointNeighborhood(
            center,
            windowDays: 7,
            minTrendIndicators: 3,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ct: CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);

        var payload = ok!.Value as InsightsController.TurningPointNeighborhoodAnalysisDto;
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.Entries.Count, Is.EqualTo(15));

        var boostedDay = payload.Entries.Single(e => e.TimestampUtc.Date == center.AddDays(1).Date);
        Assert.That(payload.MinTrendIndicators, Is.EqualTo(3));
        Assert.That(boostedDay.Bullish.AlignedRanges, Is.GreaterThanOrEqualTo(3));
        Assert.That(boostedDay.Bullish.AlignedAnchorRanges, Is.GreaterThanOrEqualTo(1));
        Assert.That(boostedDay.Bullish.HasMultipleTrendIndicators, Is.True);
        Assert.That(boostedDay.Bullish.PassesRule, Is.True);
        Assert.That(boostedDay.IsBullishConditionCandidate, Is.True);
    }

    [Test]
    public async Task GetTurningPointNeighborhood_WhenMinTrendIndicatorsTooHigh_DisablesCandidate()
    {
        var center = new DateTime(2023, 6, 14, 23, 59, 59, DateTimeKind.Utc);
        var candles = BuildDailyCandles(center.AddDays(-50), 101, "BTC1D");

        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("BTC1D", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        _statsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime ts, bool isOscillator, string timeRange, CancellationToken _) =>
                BuildStatistics(ts, isOscillator, timeRange, center));

        var result = await _sut.GetTurningPointNeighborhood(
            center,
            windowDays: 7,
            minTrendIndicators: 10,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ct: CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);

        var payload = ok!.Value as InsightsController.TurningPointNeighborhoodAnalysisDto;
        Assert.That(payload, Is.Not.Null);

        var boostedDay = payload!.Entries.Single(e => e.TimestampUtc.Date == center.AddDays(1).Date);
        Assert.That(boostedDay.Bullish.TrendCountSum, Is.EqualTo(6));
        Assert.That(boostedDay.Bullish.HasMultipleTrendIndicators, Is.False);
        Assert.That(boostedDay.Bullish.PassesRule, Is.False);
        Assert.That(boostedDay.IsBullishConditionCandidate, Is.False);
    }

    private static IReadOnlyList<StockCandle> BuildDailyCandles(DateTime startUtc, int count, string symbol)
    {
        var list = new List<StockCandle>(count);
        var price = 20000m;

        for (var i = 0; i < count; i++)
        {
            // Mild deterministic wave to avoid perfectly monotonic data.
            var drift = (i % 11) - 5;
            price += drift;
            if (price < 1000m)
            {
                price = 1000m;
            }

            list.Add(new StockCandle
            {
                Symbol = symbol,
                Timestamp = startUtc.AddDays(i),
                OpenPrice = price,
                HighPrice = price + 50,
                LowPrice = price - 50,
                ClosePrice = price,
                Volume = 1000 + i
            });
        }

        return list;
    }

    private static BitcoinStatistics BuildStatistics(DateTime timestampUtc, bool isOscillator, string timeRange, DateTime center)
    {
        var boostedRanges = new HashSet<string>(StringComparer.Ordinal) { "4h", "1d", "1w" };
        var isBoostedDay = timestampUtc.Date == center.AddDays(1).Date && boostedRanges.Contains(timeRange);

        var stat = new BitcoinTimeRangeStatistics
        {
            IsOscillatorIndicatorGroup = isOscillator,
            IndicatorBuyCount = isBoostedDay ? (isOscillator ? 3 : 6) : 1,
            IndicatorSellCount = isBoostedDay ? 0 : 1,
            IndicatorStrongTrendCount = isBoostedDay && isOscillator ? 1 : 0,
            IndicatorMediumTrendCount = isBoostedDay && isOscillator ? 1 : 0
        };

        return new BitcoinStatistics
        {
            DateTime = timestampUtc,
            Price = 30000,
            TimeRangeStatistics = new[] { stat }
        };
    }
}
