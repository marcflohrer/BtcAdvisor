using Moq;
using NUnit.Framework;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class MonteCarloSignificanceServiceTests
{
    private Mock<IBacktestService> _backtestMock = null!;
    private Mock<IBinanceKlineService> _klineMock = null!;
    private MonteCarloSignificanceService _service = null!;

    private static readonly DateTime Start = new(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void SetUp()
    {
        _backtestMock = new Mock<IBacktestService>(MockBehavior.Strict);
        _klineMock = new Mock<IBinanceKlineService>(MockBehavior.Strict);
        _service = new MonteCarloSignificanceService(_backtestMock.Object, _klineMock.Object);
    }

    private static BacktestSummary BuildSummaryWithTwoDeals()
    {
        var t1Entry = new DateTime(2023, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var t1Exit = t1Entry.AddDays(30);
        var t2Entry = new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2Exit = t2Entry.AddDays(45);
        return new BacktestSummary
        {
            StartDate = Start,
            EndDate = End,
            Trades = new List<BacktestTrade>
            {
                new() { EntryTimestamp = t1Entry, EntryPrice = 20000m, Timestamp = t1Exit, Price = 23000m, ProfitPercent = 15.0, HoldHours = 720, TradeAdvice = "Sell" },
                new() { EntryTimestamp = t2Entry, EntryPrice = 25000m, Timestamp = t2Exit, Price = 27500m, ProfitPercent = 10.0, HoldHours = 1080, TradeAdvice = "Sell" },
            }
        };
    }

    private static List<OhlcvCandle> BuildFlatishCandles()
    {
        // 2h candles for full year - random walk-ish
        var list = new List<OhlcvCandle>();
        var rng = new Random(1);
        var t = Start;
        double price = 20000;
        while (t <= End)
        {
            // small daily wobble
            price *= 1 + (rng.NextDouble() - 0.5) * 0.01;
            var close = t.AddHours(2);
            list.Add(new OhlcvCandle(t, price, price, price, price, 1, close));
            t = close;
        }
        return list;
    }

    [Test]
    public async Task RunAsync_WithFixedSeed_ReturnsDeterministicResult()
    {
        _backtestMock
            .Setup(s => s.RunBacktestAsync(Start, End, It.IsAny<CancellationToken>(),
                It.IsAny<bool>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<BtcAdvisor.Services.Conditions.LongTimeframeConfluenceThresholdSet>(),
                It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(BuildSummaryWithTwoDeals());

        _klineMock
            .Setup(k => k.GetCandlesUpToAsync("2h", End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFlatishCandles());

        var r1 = await _service.RunAsync(Start, End, 500, seed: 42, CancellationToken.None);
        var r2 = await _service.RunAsync(Start, End, 500, seed: 42, CancellationToken.None);

        Assert.That(r1.ValidIterations, Is.GreaterThan(100));
        Assert.That(r1.ActualTradeCount, Is.EqualTo(2));
        Assert.That(r1.ActualTotalReturnPercent, Is.EqualTo(25.0).Within(0.01));
        Assert.That(r1.TotalReturn.PValueGreaterOrEqual, Is.EqualTo(r2.TotalReturn.PValueGreaterOrEqual));
        Assert.That(r1.Verdict, Is.Not.Empty);
        Assert.That(r1.ActualHoldingHours, Has.Count.EqualTo(2));
    }

    [Test]
    public void RunAsync_NoClosedDeals_Throws()
    {
        _backtestMock
            .Setup(s => s.RunBacktestAsync(Start, End, It.IsAny<CancellationToken>(),
                It.IsAny<bool>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<BtcAdvisor.Services.Conditions.LongTimeframeConfluenceThresholdSet>(),
                It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new BacktestSummary { StartDate = Start, EndDate = End, Trades = new List<BacktestTrade>() });

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.RunAsync(Start, End, 100, seed: 1, CancellationToken.None));
    }

    [Test]
    public void RunAsync_InvalidRange_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.RunAsync(End, Start, 100, seed: 1, CancellationToken.None));
    }
}
