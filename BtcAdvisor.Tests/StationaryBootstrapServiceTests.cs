using Moq;
using NUnit.Framework;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class StationaryBootstrapServiceTests
{
    private Mock<IBacktestService> _backtestMock = null!;
    private Mock<IBinanceKlineService> _klineMock = null!;
    private StationaryBootstrapService _service = null!;

    private static readonly DateTime Start = new(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void SetUp()
    {
        _backtestMock = new Mock<IBacktestService>(MockBehavior.Strict);
        _klineMock = new Mock<IBinanceKlineService>(MockBehavior.Strict);
        _service = new StationaryBootstrapService(_backtestMock.Object, _klineMock.Object);
    }

    private void SetupBacktest(BacktestSummary summary)
    {
        _backtestMock
            .Setup(s => s.RunBacktestAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<LongTimeframeConfluenceThresholdSet>(),
                It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(summary);
    }

    private void SetupCandles(IReadOnlyList<OhlcvCandle> candles)
    {
        _klineMock
            .Setup(s => s.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
    }

    private static List<OhlcvCandle> MakeCandles(int days, double startPrice, double dailyDriftPercent, int seed)
    {
        var rng = new Random(seed);
        var list = new List<OhlcvCandle>(days);
        var price = startPrice;
        var t = Start;
        for (int i = 0; i < days; i++)
        {
            // Geometric drift with bounded noise.
            var noise = (rng.NextDouble() - 0.5) * 0.04;
            price *= 1.0 + dailyDriftPercent / 100.0 + noise;
            if (price <= 0) price = 1.0;
            var close = t.AddHours(24);
            list.Add(new OhlcvCandle(t, price, price, price, price, 1.0, close));
            t = close;
        }
        return list;
    }

    private static BacktestSummary EmptySummary() => new()
    {
        TotalBuys = 0,
        TotalSells = 0,
        TotalProfitPercent = 0,
        BuyAndHoldPercent = 0,
        Trades = new List<BacktestTrade>(),
    };

    [Test]
    public async Task RunAsync_DeterministicWithSeed_ProducesIdenticalResults()
    {
        SetupBacktest(EmptySummary());
        SetupCandles(MakeCandles(180, 30000, 0.05, 1));

        var r1 = await _service.RunAsync(Start, End, iterations: 500, meanBlockLength: 20, seed: 42, CancellationToken.None);
        var r2 = await _service.RunAsync(Start, End, iterations: 500, meanBlockLength: 20, seed: 42, CancellationToken.None);

        Assert.That(r1.AlphaPValue, Is.EqualTo(r2.AlphaPValue));
        Assert.That(r1.BuyAndHoldDistribution.Mean, Is.EqualTo(r2.BuyAndHoldDistribution.Mean));
        Assert.That(r1.BuyAndHoldDistribution.P05, Is.EqualTo(r2.BuyAndHoldDistribution.P05));
        Assert.That(r1.BuyAndHoldDistribution.P95, Is.EqualTo(r2.BuyAndHoldDistribution.P95));
    }

    [Test]
    public void RunAsync_InvalidDateRange_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.RunAsync(End, Start, 100, 20, 1, CancellationToken.None));
    }

    [Test]
    public void RunAsync_NonPositiveIterations_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.RunAsync(Start, End, 0, 20, 1, CancellationToken.None));
    }

    [Test]
    public void RunAsync_InvalidBlockLength_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.RunAsync(Start, End, 100, 0, 1, CancellationToken.None));
    }

    [Test]
    public void RunAsync_TooFewCandles_Throws()
    {
        SetupBacktest(EmptySummary());
        SetupCandles(MakeCandles(10, 30000, 0.05, 1));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.RunAsync(Start, End, 100, 20, 1, CancellationToken.None));
    }

    [Test]
    public async Task RunAsync_AlphaPValueWithinUnitInterval()
    {
        SetupBacktest(EmptySummary());
        SetupCandles(MakeCandles(120, 30000, 0.05, 7));

        var r = await _service.RunAsync(Start, End, iterations: 300, meanBlockLength: 15, seed: 99, CancellationToken.None);

        Assert.That(r.AlphaPValue, Is.GreaterThanOrEqualTo(0.0));
        Assert.That(r.AlphaPValue, Is.LessThanOrEqualTo(1.0));
        Assert.That(r.TradingDays, Is.EqualTo(119));
        Assert.That(r.Verdict, Is.Not.Empty);
    }

    [Test]
    public async Task RunAsync_NoTrades_StrategyZeroTimeInMarket()
    {
        SetupBacktest(EmptySummary());
        SetupCandles(MakeCandles(120, 30000, 0.05, 11));

        var r = await _service.RunAsync(Start, End, iterations: 200, meanBlockLength: 10, seed: 5, CancellationToken.None);

        Assert.That(r.TimeInMarketPercent, Is.EqualTo(0.0));
        Assert.That(r.ActualStrategyCompoundedPercent, Is.EqualTo(0.0));
    }

    [Test]
    public async Task RunAsync_AlwaysInPosition_StrategyMatchesBnH()
    {
        var candles = MakeCandles(120, 30000, 0.03, 13);
        // Single trade covering the entire window.
        var trade = new BacktestTrade
        {
            Timestamp = candles[^1].CloseTime,
            EntryTimestamp = candles[0].CloseTime.AddDays(-1),
            EntryPrice = (decimal)candles[0].Close,
            Price = (decimal)candles[^1].Close,
            ProfitPercent = 0.0,
            TradeAdvice = "sell",
        };
        var summary = EmptySummary();
        summary.Trades.Add(trade);
        SetupBacktest(summary);
        SetupCandles(candles);

        var r = await _service.RunAsync(Start, End, iterations: 200, meanBlockLength: 10, seed: 17, CancellationToken.None);

        Assert.That(r.TimeInMarketPercent, Is.EqualTo(100.0));
        Assert.That(r.ActualStrategyCompoundedPercent, Is.EqualTo(r.ActualBuyAndHoldCompoundedPercent).Within(0.01));
        Assert.That(r.ActualAlphaPercent, Is.EqualTo(0.0).Within(0.01));
    }
}
