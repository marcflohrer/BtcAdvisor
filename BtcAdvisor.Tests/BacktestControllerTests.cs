using Microsoft.AspNetCore.Mvc;
using Moq;
using BtcAdvisor.Controllers;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class BacktestControllerTests
{
    private Mock<IBacktestService> _mockBacktestService;
    private Mock<IMonteCarloSignificanceService> _mockMonteCarloService;
    private Mock<IBinanceKlineService> _mockKlineService;
    private Mock<IRegimeStabilityService> _mockRegimeStabilityService;
    private Mock<IStationaryBootstrapService> _mockBootstrapService;
    private BacktestController _sut;

    private static readonly DateTime ValidStart = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ValidEnd = new(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void Setup()
    {
        _mockBacktestService = new Mock<IBacktestService>(MockBehavior.Strict);
        _mockMonteCarloService = new Mock<IMonteCarloSignificanceService>(MockBehavior.Strict);
        _mockKlineService = new Mock<IBinanceKlineService>(MockBehavior.Strict);
        _mockRegimeStabilityService = new Mock<IRegimeStabilityService>(MockBehavior.Strict);
        _mockBootstrapService = new Mock<IStationaryBootstrapService>(MockBehavior.Strict);
        _sut = new BacktestController(_mockBacktestService.Object, _mockMonteCarloService.Object, _mockKlineService.Object, _mockRegimeStabilityService.Object, _mockBootstrapService.Object);
    }

    [Test]
    public async Task RunBacktest_WhenEndDateBeforeStartDate_ReturnsBadRequest()
    {
        // Arrange
        var start = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = await _sut.RunBacktest(start, end, false, cancellationToken: CancellationToken.None);

        // Assert
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task RunBacktest_WhenEndDateEqualsStartDate_ReturnsBadRequest()
    {
        // Arrange
        var date = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = await _sut.RunBacktest(date, date, false, cancellationToken: CancellationToken.None);

        // Assert
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task RunBacktest_ValidRequest_ReturnsOkWithSummary()
    {
        // Arrange
        var expected = new BacktestSummary
        {
            StartDate = ValidStart,
            EndDate = ValidEnd,
            TimestampsEvaluated = 42,
            TotalBuys = 5,
            TotalSells = 4,
            TotalProfitPercent = 18.5,
            WinRatePercent = 75.0
        };
        _mockBacktestService
            .Setup(s => s.RunBacktestAsync(ValidStart, ValidEnd, It.IsAny<CancellationToken>(), false,
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<LongTimeframeConfluenceThresholdSet>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.RunBacktest(ValidStart, ValidEnd, false, cancellationToken: CancellationToken.None);

        // Assert
        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        Assert.That(ok!.Value, Is.SameAs(expected));
    }

    [Test]
    public async Task RunBacktest_WhenServiceThrows_ReturnsInternalServerError()
    {
        // Arrange
        _mockBacktestService
            .Setup(s => s.RunBacktestAsync(ValidStart, ValidEnd, It.IsAny<CancellationToken>(), false,
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<LongTimeframeConfluenceThresholdSet>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>()))
            .ThrowsAsync(new InvalidOperationException("DB down"));

        // Act
        var result = await _sut.RunBacktest(ValidStart, ValidEnd, false, cancellationToken: CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task RunBacktest_ValidRequest_CallsServiceWithCorrectDates()
    {
        // Arrange
        _mockBacktestService
            .Setup(s => s.RunBacktestAsync(ValidStart, ValidEnd, It.IsAny<CancellationToken>(), false,
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<LongTimeframeConfluenceThresholdSet>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>()))
            .ReturnsAsync(new BacktestSummary());

        // Act
        await _sut.RunBacktest(ValidStart, ValidEnd, false, cancellationToken: CancellationToken.None);

        // Assert
        _mockBacktestService.Verify(
            s => s.RunBacktestAsync(ValidStart, ValidEnd, It.IsAny<CancellationToken>(), false,
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<LongTimeframeConfluenceThresholdSet>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>()),
            Times.Once);
    }

    [Test]
    public async Task RunBacktest_WithPersistTrue_CallsServiceWithPersistTrue()
    {
        // Arrange
        _mockBacktestService
            .Setup(s => s.RunBacktestAsync(ValidStart, ValidEnd, It.IsAny<CancellationToken>(), true,
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<LongTimeframeConfluenceThresholdSet>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>()))
            .ReturnsAsync(new BacktestSummary());

        // Act
        var result = await _sut.RunBacktest(ValidStart, ValidEnd, true, cancellationToken: CancellationToken.None);

        // Assert
        _mockBacktestService.Verify(
            s => s.RunBacktestAsync(ValidStart, ValidEnd, It.IsAny<CancellationToken>(), true,
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<LongTimeframeConfluenceThresholdSet>(), It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>()),
            Times.Once);
        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }

    [Test]
    public async Task RunBacktest_WithCustomConfluenceThresholds_ForwardsThresholdSet()
    {
        _mockBacktestService
            .Setup(s => s.RunBacktestAsync(
                ValidStart,
                ValidEnd,
                It.IsAny<CancellationToken>(),
                false,
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.Is<LongTimeframeConfluenceThresholdSet>(t =>
                    t.BullishMinAlignedRanges == 2 &&
                    t.BullishMinTrendCountSum == 2 &&
                    t.BearishMinAlignedRanges == 4 &&
                    t.BearishMinTrendCountSum == 3),
                It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>()))
            .ReturnsAsync(new BacktestSummary());

        await _sut.RunBacktest(
            ValidStart,
            ValidEnd,
            false,
            bullishMinAlignedRanges: 2,
            bullishMinTrendCountSum: 2,
            bearishMinAlignedRanges: 4,
            bearishMinTrendCountSum: 3,
            cancellationToken: CancellationToken.None);

        _mockBacktestService.VerifyAll();
    }

    [Test]
    public async Task RunBacktest_WithCustomBearishRegimeLinearThreshold_ForwardsThreshold()
    {
        _mockBacktestService
            .Setup(s => s.RunBacktestAsync(
                ValidStart,
                ValidEnd,
                It.IsAny<CancellationToken>(),
                false,
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<LongTimeframeConfluenceThresholdSet>(),
                25d,
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<double>()))
            .ReturnsAsync(new BacktestSummary());

        await _sut.RunBacktest(
            ValidStart,
            ValidEnd,
            false,
            bearishRegimeMinLinearBullishScore: 25d,
            cancellationToken: CancellationToken.None);

        _mockBacktestService.VerifyAll();
    }

    [Test]
    public async Task Diagnostics_ValidRequest_ReturnsOkWithDiagnostics()
    {
        var expected = new BacktestDecisionDiagnostics
        {
            StartDate = ValidStart,
            EndDate = ValidEnd,
            TimestampsEvaluated = 42,
            RawBuySignals = 5,
            RawSellSignals = 4,
            RawWaitSignals = 33
        };
        _mockBacktestService
            .Setup(s => s.GetDecisionDiagnosticsAsync(ValidStart, ValidEnd, It.IsAny<CancellationToken>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<LongTimeframeConfluenceThresholdSet>(), It.IsAny<double>()))
            .ReturnsAsync(expected);

        var result = await _sut.Diagnostics(ValidStart, ValidEnd, cancellationToken: CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        Assert.That(ok!.Value, Is.SameAs(expected));
    }

    [Test]
    public async Task Diagnostics_WhenServiceThrows_ReturnsInternalServerError()
    {
        _mockBacktestService
            .Setup(s => s.GetDecisionDiagnosticsAsync(ValidStart, ValidEnd, It.IsAny<CancellationToken>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<LongTimeframeConfluenceThresholdSet>(), It.IsAny<double>()))
            .ThrowsAsync(new InvalidOperationException("diagnostics failed"));

        var result = await _sut.Diagnostics(ValidStart, ValidEnd, cancellationToken: CancellationToken.None);

        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task RunWalkForward_DelegatesToService()
    {
        var row = new BacktestWalkForwardFoldRow(
            Fold: 1,
            TrainStart: ValidStart,
            TrainEnd: ValidStart.AddMonths(12).AddTicks(-1),
            TestStart: ValidStart.AddMonths(12),
            TestEnd: ValidStart.AddMonths(15).AddTicks(-1),
            BestMomentumBrakeMaxRatio: 0.25,
            BestTroughMinDrawdownPercent: 10,
            BestTroughRecoveryProximityPercent: 5,
            BestBearishRegimeMinLinearBullishScore: 22,
            TrainTotalProfitPercent: -5,
            TrainBuyAndHoldPercent: 7,
            TrainDelta: -12,
            TestTotalProfitPercent: 2,
            TestBuyAndHoldPercent: 4,
            TestDelta: -2,
            TestTotalBuys: 1,
            TestTotalSells: 1,
            TestWinRatePercent: 100,
            TestMaxLossPercent: 2,
            TestMaxGainPercent: 2);

        _mockBacktestService
            .Setup(s => s.WalkForwardAsync(
                ValidStart,
                ValidEnd,
                12,
                3,
                1,
                It.IsAny<IReadOnlyList<double>>(),
                It.IsAny<IReadOnlyList<double>>(),
                It.IsAny<IReadOnlyList<double>>(),
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<IReadOnlyList<int>>(),
                It.IsAny<IReadOnlyList<double>>(),
                It.IsAny<IReadOnlyList<bool>>(),
                It.IsAny<IReadOnlyList<bool>>(),
                It.IsAny<IReadOnlyList<double>>(),
                It.IsAny<CancellationToken>()))
            .Returns(GetRows());

        async IAsyncEnumerable<BacktestWalkForwardFoldRow> GetRows()
        {
            yield return row;
            await Task.CompletedTask;
        }

        var result = _sut.RunWalkForward(ValidStart, ValidEnd, cancellationToken: CancellationToken.None);
        var rows = await result.ToListAsync();

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0], Is.EqualTo(row));
    }

    // ── Split-Validation ──────────────────────────────────────────────────────

    private static readonly DateTime SplitStart = new(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SplitMid = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SplitEnd = new(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task RunSplitValidation_WhenSplitDateBeforeStartDate_ReturnsBadRequest()
    {
        var result = await _sut.RunSplitValidation(
            startDate: SplitMid, splitDate: SplitStart, endDate: SplitEnd,
            cancellationToken: CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task RunSplitValidation_WhenEndDateBeforeSplitDate_ReturnsBadRequest()
    {
        var result = await _sut.RunSplitValidation(
            startDate: SplitStart, splitDate: SplitEnd, endDate: SplitMid,
            cancellationToken: CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task RunSplitValidation_ValidRequest_ReturnsOkWithResult()
    {
        // Arrange
        var expected = new BacktestSplitValidationResult
        {
            SplitDate = SplitMid,
            InSample = new BacktestSummary { StartDate = SplitStart, EndDate = SplitMid },
            OutOfSample = new BacktestSummary { StartDate = SplitMid, EndDate = SplitEnd },
            InSampleStrategyVsHodlDelta = 15.0,
            OutOfSampleStrategyVsHodlDelta = 8.0,
            StrategyVsHodlDegradation = 7.0
        };
        _mockBacktestService
            .Setup(s => s.RunSplitValidationAsync(SplitStart, SplitMid, SplitEnd, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(expected);

        // Act
        var result = await _sut.RunSplitValidation(
            startDate: SplitStart, splitDate: SplitMid, endDate: SplitEnd,
            cancellationToken: CancellationToken.None);

        // Assert
        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        Assert.That(ok!.Value, Is.SameAs(expected));
    }

    [Test]
    public async Task RunSplitValidation_WhenServiceThrows_ReturnsInternalServerError()
    {
        // Arrange
        _mockBacktestService
            .Setup(s => s.RunSplitValidationAsync(SplitStart, SplitMid, SplitEnd, It.IsAny<CancellationToken>(), true))
            .ThrowsAsync(new InvalidOperationException("DB down"));

        // Act
        var result = await _sut.RunSplitValidation(
            startDate: SplitStart, splitDate: SplitMid, endDate: SplitEnd,
            cancellationToken: CancellationToken.None);

        // Assert
        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(500));
    }

    // ── Gate-Ablation ─────────────────────────────────────────────────────────

    [Test]
    public async Task RunGateAblation_ValidRequest_DelegatesToServiceAndReturnsRows()
    {
        // Arrange
        var expectedRow = new BacktestGateAblationRow(
            "Baseline", "all gates active (appsettings)", "—",
            10.0, 8.0, 2.0, 0.0, "Reference");

        _mockBacktestService
            .Setup(s => s.RunGateAblationAsync(SplitStart, SplitMid, SplitEnd, It.IsAny<CancellationToken>()))
            .Returns(GetRows());

        async IAsyncEnumerable<BacktestGateAblationRow> GetRows()
        {
            yield return expectedRow;
            await Task.CompletedTask;
        }

        // Act
        var result = _sut.RunGateAblation(SplitStart, SplitMid, SplitEnd, CancellationToken.None);
        var rows = await result.ToListAsync();

        // Assert
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0], Is.EqualTo(expectedRow));
    }

    [Test]
    public async Task RunGateAblation_EmptyService_ReturnsEmptySequence()
    {
        // Arrange
        _mockBacktestService
            .Setup(s => s.RunGateAblationAsync(SplitStart, SplitMid, SplitEnd, It.IsAny<CancellationToken>()))
            .Returns(EmptyRows());

        async IAsyncEnumerable<BacktestGateAblationRow> EmptyRows()
        {
            await Task.CompletedTask;
            yield break;
        }

        // Act
        var result = _sut.RunGateAblation(SplitStart, SplitMid, SplitEnd, CancellationToken.None);
        var rows = await result.ToListAsync();

        // Assert
        Assert.That(rows, Is.Empty);
    }
}
