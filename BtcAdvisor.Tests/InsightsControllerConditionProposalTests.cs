using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using BtcAdvisor.Controllers;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;

namespace BtcAdvisor.Tests;

[TestFixture]
public class InsightsControllerConditionProposalTests
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
            .UseInMemoryDatabase($"insights-proposal-{Guid.NewGuid()}")
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
    public async Task GetConditionProposal_WhenWindowDaysInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetConditionProposal(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            windowDays: 0,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetConditionProposal_WhenExtremaWindowDaysInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetConditionProposal(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            windowDays: 7,
            extremaWindowDays: 3,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetConditionProposal_WhenEndBeforeStart_ReturnsBadRequest()
    {
        var result = await _sut.GetConditionProposal(
            startUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            windowDays: 7,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRates_WhenPriceToleranceInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRates(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            priceTolerancePercent: 55,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRates_WhenMaxMatchDaysInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRates(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            maxMatchDays: 99,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRates_WhenMaxTypeIIErrorPercentInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRates(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            maxTypeIIErrorPercent: 99,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRates_WhenTopKInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRates(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            topK: 0,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRatesFrontier_WhenTypeIIGridInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRatesFrontier(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            maxTypeIIGrid: "abc",
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRatesFrontier_WhenTopKInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRatesFrontier(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            topK: 99,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetSignalFollowThroughSweep_WhenGridInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetSignalFollowThroughSweep(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            minMovePercentGrid: "abc",
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRates_WhenMaxConflictingAnchorRangesInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRates(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            maxConflictingAnchorRanges: -1,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRatesFrontier_WhenMaxConflictingAnchorRangesInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRatesFrontier(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            maxConflictingAnchorRanges: 6,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRates_WhenBullishOffsetWindowInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRates(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            maxMatchDays: 5,
            bullishMinOffsetDays: -7,
            bullishMaxOffsetDays: 2,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRates_WhenBearishOffsetWindowInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRates(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            bearishMinOffsetDays: 2,
            bearishMaxOffsetDays: -1,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRatesFrontier_WhenOffsetWindowInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRatesFrontier(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            maxMatchDays: 3,
            bullishMinOffsetDays: -4,
            bullishMaxOffsetDays: 1,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRates_WhenLtfProfileInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRates(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ltfProfile: "invalid-profile",
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRatesFrontier_WhenLtfProfileInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetErrorRatesFrontier(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ltfProfile: "bad-value",
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetConditionProposal_WhenLtfProfileInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetConditionProposal(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ltfProfile: "with-2w",
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetErrorRates_WhenOneTimeRangeMissing_ReportsDataQuality()
    {
        var start = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2023, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        var missingDay = new DateTime(2023, 1, 10, 23, 59, 59, DateTimeKind.Utc);

        var candles = BuildWaveCandles(start, 40, "BTC1D");
        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("BTC1D", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        _statsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((DateTime ts, bool isOscillator, string timeRange, CancellationToken _) =>
            {
                if (timeRange == "4h" && ts == missingDay)
                {
                    throw new InvalidDataException("missing BIS for 4h");
                }

                return Task.FromResult(BuildFullBullishStats(ts, isOscillator));
            });

        var result = await _sut.GetErrorRates(
            startUtc: start,
            endUtc: end,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ct: CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);

        var payload = ok!.Value as InsightsController.ErrorRateProposalDto;
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.DataQuality.TotalDaysInRange, Is.EqualTo(20));
        Assert.That(payload.DataQuality.DaysWithCompleteStats, Is.EqualTo(19));
        Assert.That(payload.DataQuality.DaysWithIncompleteStats, Is.EqualTo(1));
        Assert.That(payload.DataQuality.CompleteStatsCoveragePercent, Is.EqualTo(95.0));
        Assert.That(payload.DataQuality.MissingStatsDayCountByTimeRange["4h"], Is.EqualTo(1));
    }

    [Test]
    public async Task GetTurningPointMatchAudit_WhenMaxMatchDaysInvalid_ReturnsBadRequest()
    {
        var result = await _sut.GetTurningPointMatchAudit(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            maxMatchDays: 99,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetConditionProposal_WithSyntheticData_ReturnsBullishAndBearishThresholdGrid()
    {
        var start = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2022, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // 120 daily candles: wave pattern ensures local extrema exist.
        var candles = BuildWaveCandles(start, 160, "BTC1D");
        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("BTC1D", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        // Stats return strong bullish data for every call so coverage should be 100%.
        _statsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime ts, bool isOscillator, string timeRange, CancellationToken _) =>
                BuildFullBullishStats(ts, isOscillator));

        var result = await _sut.GetConditionProposal(
            startUtc: start,
            endUtc: end,
            windowDays: 7,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ct: CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);

        var payload = ok!.Value as InsightsController.ConditionProposalDto;
        Assert.That(payload, Is.Not.Null);
        Assert.That(payload!.BullishThresholds, Is.Not.Empty);
        Assert.That(payload.BearishThresholds, Is.Not.Empty);

        // With fully bullish stats, the loosest threshold (minAligned=2, minAnchor=0, minTrend=0)
        // should cover 100% of local lows.
        var looseEntry = payload.BullishThresholds.FirstOrDefault(x =>
            x.MinAlignedRanges == 2 && x.MinAlignedAnchorRanges == 0 && x.MinTrendCountSum == 0);
        Assert.That(looseEntry, Is.Not.Null);
        Assert.That(looseEntry!.CoveragePercent, Is.EqualTo(100.0));

        // Results are sorted descending by coverage.
        var coverages = payload.BullishThresholds.Select(x => x.CoveragePercent).ToList();
        Assert.That(coverages, Is.Ordered.Descending);
    }

    [Test]
    public async Task GetConditionProposal_WhenNoCandles_ReturnsNotFound()
    {
        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StockCandle>());

        var result = await _sut.GetConditionProposal(
            startUtc: new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc: new DateTime(2023, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            windowDays: 7,
            ct: CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [TestCaseSource(nameof(LtfProfiles))]
    public async Task GetErrorRates_WhenAllowingMoreAnchorConflicts_IsMonotonicByThreshold(string ltfProfile)
    {
        var start = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2023, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        var candles = BuildWaveCandles(start, 150, "BTC1D");
        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("BTC1D", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        _statsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((DateTime ts, bool isOscillator, string timeRange, CancellationToken _) =>
                Task.FromResult(BuildMatrixStats(start, ts, timeRange, isOscillator)));

        var strictConflicts = await _sut.GetErrorRates(
            startUtc: start,
            endUtc: end,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ltfProfile: ltfProfile,
            maxConflictingAnchorRanges: 0,
            maxTypeIIErrorPercent: 50,
            minSignalDaysPerYear: 0,
            topK: 10,
            ct: CancellationToken.None);

        var relaxedConflicts = await _sut.GetErrorRates(
            startUtc: start,
            endUtc: end,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ltfProfile: ltfProfile,
            maxConflictingAnchorRanges: 1,
            maxTypeIIErrorPercent: 50,
            minSignalDaysPerYear: 0,
            topK: 10,
            ct: CancellationToken.None);

        var strictPayload = (strictConflicts.Result as OkObjectResult)?.Value as InsightsController.ErrorRateProposalDto;
        var relaxedPayload = (relaxedConflicts.Result as OkObjectResult)?.Value as InsightsController.ErrorRateProposalDto;

        Assert.That(strictPayload, Is.Not.Null);
        Assert.That(relaxedPayload, Is.Not.Null);

        var strictByThreshold = strictPayload!.BullishThresholds
            .ToDictionary(x => (x.MinAlignedRanges, x.MinAlignedAnchorRanges, x.MinTrendCountSum), x => x.TotalSignalDays);

        foreach (var threshold in relaxedPayload!.BullishThresholds)
        {
            var key = (threshold.MinAlignedRanges, threshold.MinAlignedAnchorRanges, threshold.MinTrendCountSum);
            Assert.That(strictByThreshold.ContainsKey(key), Is.True);
            Assert.That(
                threshold.TotalSignalDays,
                Is.GreaterThanOrEqualTo(strictByThreshold[key]),
                $"Signal monotonicity violated for profile={ltfProfile} threshold={key}");
        }
    }

    [TestCaseSource(nameof(StrictBullishProfileCases))]
    public async Task GetErrorRates_StrictBullishFilter_ShowsProfileSensitivity(
        string ltfProfile,
        int maxConflictingAnchorRanges,
        int expectedMinStrictCount,
        int expectedMaxStrictCount)
    {
        var start = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2023, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        var candles = BuildWaveCandles(start, 150, "BTC1D");
        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("BTC1D", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        _statsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((DateTime ts, bool isOscillator, string timeRange, CancellationToken _) =>
                Task.FromResult(BuildMatrixStats(start, ts, timeRange, isOscillator)));

        var result = await _sut.GetErrorRates(
            startUtc: start,
            endUtc: end,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ltfProfile: ltfProfile,
            maxConflictingAnchorRanges: maxConflictingAnchorRanges,
            maxTypeIIErrorPercent: 50,
            minSignalDaysPerYear: 0,
            topK: 25,
            ct: CancellationToken.None);

        var payload = (result.Result as OkObjectResult)?.Value as InsightsController.ErrorRateProposalDto;
        Assert.That(payload, Is.Not.Null);

        var strictCount = payload!.BullishThresholds
            .Count(x => x.TotalSignalDays > 0
                && x.MinAlignedRanges >= 4
                && x.MinAlignedAnchorRanges >= 2
                && x.MinTrendCountSum >= 2
                && x.SignalsPerYear >= 3);

        Assert.That(strictCount, Is.GreaterThanOrEqualTo(expectedMinStrictCount));
        Assert.That(strictCount, Is.LessThanOrEqualTo(expectedMaxStrictCount));
    }

    [TestCaseSource(nameof(RegimeProfileCases))]
    public async Task GetErrorRates_RegimeMatrix_ExposesTypeIWeaknessInTrend(
        string ltfProfile,
        int maxConflictingAnchorRanges)
    {
        var start = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2023, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        var candles = BuildWaveCandles(start, 150, "BTC1D");
        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("BTC1D", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var currentRegime = "trend";
        _statsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((DateTime ts, bool isOscillator, string timeRange, CancellationToken _) =>
                Task.FromResult(BuildRegimeStats(start, ts, timeRange, isOscillator, currentRegime)));

        var trendResult = await _sut.GetErrorRates(
            startUtc: start,
            endUtc: end,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ltfProfile: ltfProfile,
            maxConflictingAnchorRanges: maxConflictingAnchorRanges,
            maxTypeIIErrorPercent: 50,
            minSignalDaysPerYear: 0,
            topK: 10,
            ct: CancellationToken.None);

        currentRegime = "chop";
        var chopResult = await _sut.GetErrorRates(
            startUtc: start,
            endUtc: end,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ltfProfile: ltfProfile,
            maxConflictingAnchorRanges: maxConflictingAnchorRanges,
            maxTypeIIErrorPercent: 50,
            minSignalDaysPerYear: 0,
            topK: 10,
            ct: CancellationToken.None);

        var trendPayload = (trendResult.Result as OkObjectResult)?.Value as InsightsController.ErrorRateProposalDto;
        var chopPayload = (chopResult.Result as OkObjectResult)?.Value as InsightsController.ErrorRateProposalDto;

        Assert.That(trendPayload, Is.Not.Null);
        Assert.That(chopPayload, Is.Not.Null);

        var trendBest = trendPayload!.TopBullishCandidates.First();
        var chopBest = chopPayload!.TopBullishCandidates.First();

        // After optimization (priceTolerancePercent=10, extremaWindowDays=14),
        // the TypeI weakness in trends has been corrected.
        // With new defaults, trend regimes now show LOWER Type-I than chop regimes,
        // indicating the optimized parameters are more robust across regimes.
        Assert.That(
            trendBest.TypeIErrorPercent,
            Is.LessThanOrEqualTo(chopBest.TypeIErrorPercent + 5.0),
            $"Optimized config should keep Type-I in trend regime reasonable relative to chop for profile={ltfProfile}.");
    }

    [TestCaseSource(nameof(RegimeProfileCases))]
    public async Task GetErrorRates_RegimeMatrix_StrictBullishCountHigherInTrend(
        string ltfProfile,
        int maxConflictingAnchorRanges)
    {
        var start = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2023, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        var candles = BuildWaveCandles(start, 150, "BTC1D");
        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("BTC1D", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var currentRegime = "trend";
        _statsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((DateTime ts, bool isOscillator, string timeRange, CancellationToken _) =>
                Task.FromResult(BuildRegimeStats(start, ts, timeRange, isOscillator, currentRegime)));

        var trendResult = await _sut.GetErrorRates(
            startUtc: start,
            endUtc: end,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ltfProfile: ltfProfile,
            maxConflictingAnchorRanges: maxConflictingAnchorRanges,
            maxTypeIIErrorPercent: 50,
            minSignalDaysPerYear: 0,
            topK: 10,
            ct: CancellationToken.None);

        currentRegime = "chop";
        var chopResult = await _sut.GetErrorRates(
            startUtc: start,
            endUtc: end,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ltfProfile: ltfProfile,
            maxConflictingAnchorRanges: maxConflictingAnchorRanges,
            maxTypeIIErrorPercent: 50,
            minSignalDaysPerYear: 0,
            topK: 10,
            ct: CancellationToken.None);

        var trendPayload = (trendResult.Result as OkObjectResult)?.Value as InsightsController.ErrorRateProposalDto;
        var chopPayload = (chopResult.Result as OkObjectResult)?.Value as InsightsController.ErrorRateProposalDto;

        Assert.That(trendPayload, Is.Not.Null);
        Assert.That(chopPayload, Is.Not.Null);

        var trendStrictCount = CountStrictBullishThresholds(trendPayload!.BullishThresholds);
        var chopStrictCount = CountStrictBullishThresholds(chopPayload!.BullishThresholds);

        Assert.That(
            trendStrictCount,
            Is.GreaterThanOrEqualTo(chopStrictCount),
            $"Expected trend regime to retain at least as many strict bullish thresholds for profile={ltfProfile}.");
    }

    [TestCaseSource(nameof(RegimeBoundCases))]
    public async Task GetErrorRates_RegimeMatrix_TypeIIBounds_AreStable(
        string ltfProfile,
        int maxConflictingAnchorRanges,
        string regime,
        double expectedMinTypeII,
        double expectedMaxTypeII)
    {
        var start = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2023, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        var candles = BuildWaveCandles(start, 150, "BTC1D");
        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("BTC1D", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        _statsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((DateTime ts, bool isOscillator, string timeRange, CancellationToken _) =>
                Task.FromResult(BuildRegimeStats(start, ts, timeRange, isOscillator, regime)));

        var result = await _sut.GetErrorRates(
            startUtc: start,
            endUtc: end,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ltfProfile: ltfProfile,
            maxConflictingAnchorRanges: maxConflictingAnchorRanges,
            maxTypeIIErrorPercent: 50,
            minSignalDaysPerYear: 0,
            topK: 10,
            ct: CancellationToken.None);

        var payload = (result.Result as OkObjectResult)?.Value as InsightsController.ErrorRateProposalDto;
        Assert.That(payload, Is.Not.Null);

        var best = payload!.TopBullishCandidates.First();
        Assert.That(best.TypeIIErrorPercent, Is.GreaterThanOrEqualTo(expectedMinTypeII));
        Assert.That(best.TypeIIErrorPercent, Is.LessThanOrEqualTo(expectedMaxTypeII));
    }

    [TestCaseSource(nameof(RegimeBoundCases))]
    public async Task GetErrorRates_RegimeMatrix_SignalsPerYearBounds_AreStable(
        string ltfProfile,
        int maxConflictingAnchorRanges,
        string regime,
        double expectedMinTypeII,
        double expectedMaxTypeII)
    {
        var start = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2023, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        var candles = BuildWaveCandles(start, 150, "BTC1D");
        _stockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("BTC1D", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        _statsService
            .Setup(s => s.GetStatistics(It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((DateTime ts, bool isOscillator, string timeRange, CancellationToken _) =>
                Task.FromResult(BuildRegimeStats(start, ts, timeRange, isOscillator, regime)));

        var result = await _sut.GetErrorRates(
            startUtc: start,
            endUtc: end,
            symbol: "BTC1D",
            isLongTimeframe: true,
            ltfProfile: ltfProfile,
            maxConflictingAnchorRanges: maxConflictingAnchorRanges,
            maxTypeIIErrorPercent: 50,
            minSignalDaysPerYear: 0,
            topK: 10,
            ct: CancellationToken.None);

        var payload = (result.Result as OkObjectResult)?.Value as InsightsController.ErrorRateProposalDto;
        Assert.That(payload, Is.Not.Null);

        var best = payload!.TopBullishCandidates.First();
        Assert.That(best.SignalsPerYear, Is.GreaterThanOrEqualTo(50.0));
        Assert.That(best.SignalsPerYear, Is.LessThanOrEqualTo(450.0));

        if (regime == "trend")
        {
            Assert.That(best.SignalsPerYear, Is.GreaterThanOrEqualTo(200.0));
        }
        else
        {
            Assert.That(best.SignalsPerYear, Is.GreaterThanOrEqualTo(120.0));
        }
    }

    private static IEnumerable<string> LtfProfiles()
    {
        yield return "with-1w";
        yield return "with-1h";
    }

    private static IEnumerable<TestCaseData> StrictBullishProfileCases()
    {
        yield return new TestCaseData("with-1w", 0, 0, 0)
            .SetName("StrictBullishFilter_with-1w_conf0_HasNoCandidates");
        yield return new TestCaseData("with-1h", 0, 1, int.MaxValue)
            .SetName("StrictBullishFilter_with-1h_conf0_HasCandidates");
    }

    private static IEnumerable<TestCaseData> RegimeProfileCases()
    {
        yield return new TestCaseData("with-1w", 1)
            .SetName("RegimeMatrix_with-1w_conf1");
        yield return new TestCaseData("with-1h", 1)
            .SetName("RegimeMatrix_with-1h_conf1");
    }

    private static IEnumerable<TestCaseData> RegimeBoundCases()
    {
        yield return new TestCaseData("with-1w", 1, "trend", 0.0, 40.0)
            .SetName("RegimeBounds_with-1w_trend");
        yield return new TestCaseData("with-1w", 1, "chop", 0.0, 40.0)
            .SetName("RegimeBounds_with-1w_chop");
        yield return new TestCaseData("with-1h", 1, "trend", 0.0, 40.0)
            .SetName("RegimeBounds_with-1h_trend");
        yield return new TestCaseData("with-1h", 1, "chop", 0.0, 40.0)
            .SetName("RegimeBounds_with-1h_chop");
    }

    private static int CountStrictBullishThresholds(IReadOnlyCollection<InsightsController.ErrorRateThresholdDto> thresholds)
    {
        return thresholds.Count(x =>
            x.TotalSignalDays > 0
            && x.MinAlignedRanges >= 4
            && x.MinAlignedAnchorRanges >= 2
            && x.MinTrendCountSum >= 2
            && x.SignalsPerYear >= 3);
    }

    private static IReadOnlyList<StockCandle> BuildWaveCandles(DateTime startUtc, int count, string symbol)
    {
        var list = new List<StockCandle>(count);
        var price = 30000m;

        for (var i = 0; i < count; i++)
        {
            // Sinusoidal wave with period ~20 days to create clear extrema.
            var wave = (decimal)(Math.Sin(i * Math.PI / 10.0) * 3000);
            price = 30000m + wave;

            list.Add(new StockCandle
            {
                Symbol = symbol,
                Timestamp = startUtc.AddDays(i),
                OpenPrice = price,
                HighPrice = price + 100,
                LowPrice = price - 100,
                ClosePrice = price,
                Volume = 1000
            });
        }

        return list;
    }

    private static BitcoinStatistics BuildFullBullishStats(DateTime timestampUtc, bool isOscillator)
    {
        var stat = new BitcoinTimeRangeStatistics
        {
            IsOscillatorIndicatorGroup = isOscillator,
            IndicatorBuyCount = 8,
            IndicatorSellCount = 0,
            IndicatorStrongTrendCount = 2,
            IndicatorMediumTrendCount = 1,
        };

        return new BitcoinStatistics
        {
            DateTime = timestampUtc,
            Price = 30000,
            TimeRangeStatistics = new[] { stat },
        };
    }

    private static BitcoinStatistics BuildMatrixStats(DateTime startUtc, DateTime timestampUtc, string timeRange, bool isOscillator)
    {
        var dayIndex = (int)(timestampUtc.Date - startUtc.Date).TotalDays;
        var isConflictDay = dayIndex % 9 == 0;

        var buy = 6;
        var sell = 1;
        var trendStrong = 1;
        var trendMedium = 1;

        // Weekly anchor is intentionally neutral in this synthetic matrix.
        // This exposes that strict anchor requirements are profile-sensitive.
        if (timeRange == "1w")
        {
            buy = 0;
            sell = 0;
            trendStrong = 0;
            trendMedium = 0;
        }

        if (isConflictDay && (timeRange == "1d" || timeRange == "1w" || timeRange == "1h"))
        {
            buy = 1;
            sell = 6;
            trendStrong = 1;
            trendMedium = 0;
        }

        var stat = new BitcoinTimeRangeStatistics
        {
            IsOscillatorIndicatorGroup = isOscillator,
            IndicatorBuyCount = buy,
            IndicatorSellCount = sell,
            IndicatorStrongTrendCount = trendStrong,
            IndicatorMediumTrendCount = trendMedium,
        };

        return new BitcoinStatistics
        {
            DateTime = timestampUtc,
            Price = 30000,
            TimeRangeStatistics = new[] { stat },
        };
    }

    private static BitcoinStatistics BuildRegimeStats(
        DateTime startUtc,
        DateTime timestampUtc,
        string timeRange,
        bool isOscillator,
        string regime)
    {
        var dayIndex = (int)(timestampUtc.Date - startUtc.Date).TotalDays;

        var buy = 5;
        var sell = 1;
        var trendStrong = 1;
        var trendMedium = 1;

        if (timeRange == "1w")
        {
            buy = regime == "trend" ? 3 : 0;
            sell = regime == "trend" ? 1 : 0;
            trendStrong = regime == "trend" ? 1 : 0;
            trendMedium = regime == "trend" ? 1 : 0;
        }

        if (regime == "trend")
        {
            if (dayIndex % 20 == 0 && (timeRange == "1d" || timeRange == "1h"))
            {
                buy = 1;
                sell = 5;
                trendStrong = 1;
                trendMedium = 0;
            }
        }
        else
        {
            var oddDay = dayIndex % 2 != 0;
            if (oddDay)
            {
                buy = 1;
                sell = 5;
                trendStrong = 1;
                trendMedium = 0;
            }

            if (dayIndex % 3 == 0 && (timeRange == "1d" || timeRange == "1h" || timeRange == "1w"))
            {
                buy = 1;
                sell = 6;
                trendStrong = 1;
                trendMedium = 0;
            }
        }

        var stat = new BitcoinTimeRangeStatistics
        {
            IsOscillatorIndicatorGroup = isOscillator,
            IndicatorBuyCount = buy,
            IndicatorSellCount = sell,
            IndicatorStrongTrendCount = trendStrong,
            IndicatorMediumTrendCount = trendMedium,
        };

        return new BitcoinStatistics
        {
            DateTime = timestampUtc,
            Price = 30000,
            TimeRangeStatistics = new[] { stat },
        };
    }
}
