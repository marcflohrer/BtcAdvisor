using Moq;
using NUnit.Framework;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class RegimeStabilityServiceTests
{
    private Mock<IBacktestService> _backtestMock = null!;
    private RegimeStabilityService _service = null!;

    private static readonly DateTime FullStart = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FullEnd = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void SetUp()
    {
        _backtestMock = new Mock<IBacktestService>(MockBehavior.Strict);
        _service = new RegimeStabilityService(_backtestMock.Object);
    }

    private static BacktestSummary MakeSummary(double totalProfit, double bnh, int buys, int sells) =>
        new()
        {
            TotalBuys = buys,
            TotalSells = sells,
            TotalProfitPercent = totalProfit,
            BuyAndHoldPercent = bnh,
            Trades = new List<BacktestTrade>(),
        };

    private void SetupAnyWindow(BacktestSummary returns)
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
            .ReturnsAsync(returns);
    }

    [Test]
    public async Task RunAsync_AllWindowsWithTrades_ReturnsCorrectStats()
    {
        SetupAnyWindow(MakeSummary(totalProfit: 80.0, bnh: 50.0, buys: 1, sells: 1));

        // 4 years, 30-month window, 12-month step → 2 full windows
        var result = await _service.RunAsync(FullStart, FullEnd, 30, 12, CancellationToken.None);

        Assert.That(result.WindowCount, Is.EqualTo(2));
        Assert.That(result.WindowsWithTrades, Is.EqualTo(2));
        Assert.That(result.WindowsWithTradesPositive, Is.EqualTo(2));
        Assert.That(result.WindowsWithTradesBeatBnH, Is.EqualTo(2));
        Assert.That(result.MedianDeltaVsBuyAndHold, Is.EqualTo(30.0).Within(0.1));
        Assert.That(result.Verdict, Does.Contain("Schlaegt BnH").Or.Contain("STARK").Or.Contain("MODERAT"));
    }

    [Test]
    public async Task RunAsync_NoTradesMarketDown_ClassifiesAsCorrectAvoidance()
    {
        SetupAnyWindow(MakeSummary(totalProfit: 0.0, bnh: -40.0, buys: 0, sells: 0));

        var result = await _service.RunAsync(FullStart, FullEnd, 30, 12, CancellationToken.None);

        Assert.That(result.WindowsNoTradesCorrectAvoidance, Is.GreaterThan(0));
        Assert.That(result.WindowsNoTradesMissedOpportunity, Is.EqualTo(0));
        Assert.That(result.Folds.All(f => f.Note.Contains("korrekt gemieden")), Is.True);
    }

    [Test]
    public async Task RunAsync_NoTradesMarketUp_ClassifiesAsMissedOpportunity()
    {
        SetupAnyWindow(MakeSummary(totalProfit: 0.0, bnh: 120.0, buys: 0, sells: 0));

        var result = await _service.RunAsync(FullStart, FullEnd, 30, 12, CancellationToken.None);

        Assert.That(result.WindowsNoTradesMissedOpportunity, Is.GreaterThan(0));
        Assert.That(result.WindowsNoTradesCorrectAvoidance, Is.EqualTo(0));
        Assert.That(result.Folds.All(f => f.Note.Contains("verpasste Chance")), Is.True);
    }

    [Test]
    public void RunAsync_InvalidRange_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _service.RunAsync(FullEnd, FullStart, 30, 12, CancellationToken.None));
    }

    [Test]
    public async Task RunAsync_WindowLargerThanRange_ProducesNoFolds()
    {
        SetupAnyWindow(MakeSummary(0, 0, 0, 0));
        // Range is 4 years = 48 months, window 60 months → 0 full windows
        var result = await _service.RunAsync(FullStart, FullEnd, 60, 12, CancellationToken.None);
        Assert.That(result.WindowCount, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_BuyWithoutSell_ClassifiesAsOpenPosition()
    {
        // Position entered but hold exceeds window end → should be WindowsWithOpenPosition, NOT missed opportunity
        SetupAnyWindow(MakeSummary(totalProfit: 0.0, bnh: 80.0, buys: 1, sells: 0));

        var result = await _service.RunAsync(FullStart, FullEnd, 30, 12, CancellationToken.None);

        Assert.That(result.WindowsWithOpenPosition, Is.GreaterThan(0));
        Assert.That(result.WindowsNoTradesMissedOpportunity, Is.EqualTo(0), "Open positions must NOT be counted as missed opportunities");
        Assert.That(result.WindowsNoTradesCorrectAvoidance, Is.EqualTo(0));
        Assert.That(result.Folds.All(f => f.Note.Contains("Position offen")), Is.True);
        Assert.That(result.Verdict, Does.Contain("Einstieg erfolgt").Or.Contain("Offen (Hold").Or.Contain("offener Position"));
    }
}
