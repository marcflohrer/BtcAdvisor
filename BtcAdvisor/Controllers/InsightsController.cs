using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Controllers;

[ApiController]
[Route("api/insights")]
public class InsightsController(
    IBitcoinStatisticsService bitcoinStatisticsService,
    ApplicationDbContext applicationDbContext,
    IStockCandleRepository stockCandleRepository,
    IBuyActionLogRepository buyActionLogRepository,
    ISellActionLogRepository sellActionLogRepository) : ControllerBase
{
    private readonly IBitcoinStatisticsService bitcoinStatisticsService = bitcoinStatisticsService;
    private readonly ApplicationDbContext applicationDbContext = applicationDbContext;
    private readonly IStockCandleRepository stockCandleRepository = stockCandleRepository;
    private readonly IBuyActionLogRepository buyActionLogRepository = buyActionLogRepository;
    private readonly ISellActionLogRepository sellActionLogRepository = sellActionLogRepository;

    public record PhaseChangeDto(
        DateTime CreatedAtUtc,
        ShortSellerState Phase,
        double BtcPrice
    );

    public record TurningPointStatsDto(
        string TimeRange,
        int OscillatorBuyCount,
        int OscillatorSellCount,
        int OscillatorStrongTrendCount,
        int OscillatorMediumTrendCount,
        int MovingAverageBuyCount,
        int MovingAverageSellCount
    );

    public record MissedTurningPointDto(
        DateTime TimestampUtc,
        decimal ClosePrice,
        string Kind,
        string ExpectedAdvice,
        IReadOnlyList<TurningPointStatsDto> Statistics
    );

    public record HeuristicProposalDto(
        string Name,
        string TargetAdvice,
        int SupportCount,
        string Description
    );

    public record MissedTurningPointsAnalysisDto(
        DateTime StartUtc,
        DateTime EndUtc,
        int MatchWindowDays,
        int TotalTurningPoints,
        int MatchedTurningPoints,
        int MissedTurningPoints,
        IReadOnlyList<MissedTurningPointDto> Missed,
        IReadOnlyList<HeuristicProposalDto> Proposals
    );

    public record ConfluenceDirectionMetricsDto(
        int AlignedRanges,
        int AlignedAnchorRanges,
        int ConflictingAnchorRanges,
        int TrendCountSum,
        bool HasMultipleTrendIndicators,
        bool PassesRule
    );

    public record TurningPointNeighborhoodEntryDto(
        DateTime TimestampUtc,
        decimal ClosePrice,
        bool IsTurningPoint,
        string? TurningPointKind,
        string? TurningPointExpectedAdvice,
        ConfluenceDirectionMetricsDto Bullish,
        ConfluenceDirectionMetricsDto Bearish,
        bool IsBullishConditionCandidate,
        bool IsBearishConditionCandidate,
        IReadOnlyList<TurningPointStatsDto> Statistics
    );

    public record TurningPointNeighborhoodAnalysisDto(
        DateTime CenterUtc,
        int WindowDays,
        int MinTrendIndicators,
        string Symbol,
        bool IsLongTimeframe,
        IReadOnlyList<TurningPointNeighborhoodEntryDto> Entries
    );

    public record TurningPointMatchAuditItemDto(
        DateTime TimestampUtc,
        decimal ClosePrice,
        string Kind,
        string ExpectedAdvice,
        bool IsMatched,
        DateTime? MatchedSignalTimestampUtc,
        decimal? MatchedSignalPrice,
        string? MatchedSignalAdvice,
        int? DayDistance,
        double? PriceDeviationPercent
    );

    public record TurningPointMatchAuditDto(
        DateTime StartUtc,
        DateTime EndUtc,
        string Symbol,
        bool IsLongTimeframe,
        double PriceTolerancePercent,
        int MaxMatchDays,
        int TotalTurningPoints,
        int MatchedCount,
        int MissedCount,
        IReadOnlyList<TurningPointMatchAuditItemDto> Items
    );

    /// <summary>
    /// Letzte drei Phasenwechsel des Shortseller-Status inkl. Zeitpunkt und BTC-Preis.
    /// </summary>
    [HttpGet("shortseller/last3")]
    public async Task<ActionResult<IEnumerable<PhaseChangeDto>>> GetLastThreeShortSellerChanges(CancellationToken ct)
    {
        var last3 = await applicationDbContext.ShortSellerPhaseNotifications
            .AsNoTracking()
            .OrderByDescending(x => x.SentAtUtc)
            .Take(3)
            .Select(x => new { x.SentAtUtc, x.ShortSellerState })
            .ToListAsync(ct);

        var results = new List<PhaseChangeDto>(last3.Count);
        foreach (var item in last3)
        {
            var stat = await bitcoinStatisticsService.GetStatistics(item.SentAtUtc.UtcDateTime, true, "1h", ct);
            results.Add(new PhaseChangeDto(
                item.SentAtUtc.UtcDateTime,
                item.ShortSellerState,
                stat?.Price ?? 0.0
            ));
        }

        return Ok(results);
    }

    /// <summary>
    /// Finds 30-day turning points (highest/lowest close in +/-30 days),
    /// checks whether an expected Buy/Sell advice exists within a match window,
    /// and returns missed points with per-timeframe statistics snapshots.
    /// </summary>
    [HttpGet("turning-points/missed")]
    public async Task<ActionResult<MissedTurningPointsAnalysisDto>> GetMissedTurningPoints(
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] int matchWindowDays = 7,
        [FromQuery] int extremaWindowDays = TurningPointDetector.DefaultWindowDays,
        [FromQuery] string symbol = "BTC/USDT",
        [FromQuery] bool isLongTimeframe = true,
        CancellationToken ct = default)
    {
        if (matchWindowDays < 0 || matchWindowDays > 30)
        {
            return BadRequest("matchWindowDays must be between 0 and 30.");
        }

        var extremaWindowValidationError = ValidateExtremaWindowDays(extremaWindowDays);
        if (extremaWindowValidationError is not null)
        {
            return extremaWindowValidationError;
        }

        var end = (endUtc ?? DateTime.UtcNow).ToUniversalTime();
        var start = (startUtc ?? new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToUniversalTime();
        if (end <= start)
        {
            return BadRequest("endUtc must be greater than startUtc.");
        }

        var candles = await stockCandleRepository.GetStockCandlesUpToAsync(symbol, end, ct);
        var inRangeCandles = candles.Where(x => x.Timestamp >= start && x.Timestamp <= end).ToList();
        if (inRangeCandles.Count == 0)
        {
            return NotFound($"No candles found for symbol '{symbol}' in selected range.");
        }

        var turningPoints = TurningPointDetector
            .DetectByWindow(inRangeCandles, extremaWindowDays)
            .OrderBy(x => x.Timestamp)
            .ToList();

        var buys = await buyActionLogRepository.GetBuyActionLogs(start, end, isLongTimeframe, ct);
        var sells = await sellActionLogRepository.GetSellActionLogs(start, end, isLongTimeframe, ct);

        var buyTimes = buys.Select(x => x.TradingDate.ToUniversalTime()).Distinct().OrderBy(x => x).ToList();
        var sellTimes = sells.Select(x => x.TradingDate.ToUniversalTime()).Distinct().OrderBy(x => x).ToList();
        var matchWindow = TimeSpan.FromDays(matchWindowDays);

        var matched = 0;
        var missed = new List<MissedTurningPointDto>();
        var timeRanges = isLongTimeframe
            ? new[] { "2h", "4h", "12h", "1d", "1w" }
            : new[] { "5m", "15m", "30m", "1h", "2h" };

        foreach (var point in turningPoints)
        {
            var expectedAdvice = point.Kind == TurningPointKind.LocalLow ? "Buy" : "Sell";
            var match = expectedAdvice == "Buy"
                ? HasMatchInWindow(buyTimes, point.Timestamp, matchWindow)
                : HasMatchInWindow(sellTimes, point.Timestamp, matchWindow);

            if (match)
            {
                matched++;
                continue;
            }

            var stats = new List<TurningPointStatsDto>(timeRanges.Length);
            foreach (var timeRange in timeRanges)
            {
                var osci = await bitcoinStatisticsService.GetStatistics(point.Timestamp, true, timeRange, ct);
                var ma = await bitcoinStatisticsService.GetStatistics(point.Timestamp, false, timeRange, ct);
                var osciStats = osci.TimeRangeStatistics.Single();
                var maStats = ma.TimeRangeStatistics.Single();

                stats.Add(new TurningPointStatsDto(
                    timeRange,
                    osciStats.IndicatorBuyCount,
                    osciStats.IndicatorSellCount,
                    osciStats.IndicatorStrongTrendCount,
                    osciStats.IndicatorMediumTrendCount,
                    maStats.IndicatorBuyCount,
                    maStats.IndicatorSellCount));
            }

            missed.Add(new MissedTurningPointDto(
                point.Timestamp,
                point.ClosePrice,
                point.Kind.ToString(),
                expectedAdvice,
                stats));
        }

        var proposals = DeriveHeuristics(missed);

        return Ok(new MissedTurningPointsAnalysisDto(
            start,
            end,
            matchWindowDays,
            turningPoints.Count,
            matched,
            missed.Count,
            missed,
            proposals));
    }

    /// <summary>
    /// Returns per-day indicator statistics and directional confluence metrics
    /// in a configurable window around a center timestamp.
    /// Useful to inspect whether stronger trend/confluence appears near a local extremum
    /// even when the extremum day itself has weak trend information.
    /// </summary>
    [HttpGet("turning-points/neighborhood")]
    public async Task<ActionResult<TurningPointNeighborhoodAnalysisDto>> GetTurningPointNeighborhood(
        [FromQuery] DateTime centerUtc,
        [FromQuery] int windowDays = 7,
        [FromQuery] int extremaWindowDays = TurningPointDetector.DefaultWindowDays,
        [FromQuery] int minTrendIndicators = 3,
        [FromQuery] int maxConflictingAnchorRanges = 0,
        [FromQuery] string symbol = "BTC1D",
        [FromQuery] bool isLongTimeframe = true,
        CancellationToken ct = default)
    {
        if (windowDays < 1 || windowDays > 30)
        {
            return BadRequest("windowDays must be between 1 and 30.");
        }

        if (minTrendIndicators < 1 || minTrendIndicators > 20)
        {
            return BadRequest("minTrendIndicators must be between 1 and 20.");
        }

        var maxConflictingAnchorRangesValidationError = ValidateMaxConflictingAnchorRanges(maxConflictingAnchorRanges);
        if (maxConflictingAnchorRangesValidationError is not null)
        {
            return maxConflictingAnchorRangesValidationError;
        }

        var extremaWindowValidationError = ValidateExtremaWindowDays(extremaWindowDays);
        if (extremaWindowValidationError is not null)
        {
            return extremaWindowValidationError;
        }

        var center = centerUtc.ToUniversalTime();
        var window = TimeSpan.FromDays(windowDays);
        var contextPadding = TimeSpan.FromDays(extremaWindowDays);
        var scanStart = center - window;
        var scanEnd = center + window;
        var loadStart = scanStart - contextPadding;
        var loadEnd = scanEnd + contextPadding;

        var candles = await stockCandleRepository.GetStockCandlesUpToAsync(symbol, loadEnd, ct);
        var loadedRange = candles
            .Where(x => x.Timestamp >= loadStart && x.Timestamp <= loadEnd)
            .OrderBy(x => x.Timestamp)
            .ToList();

        if (loadedRange.Count == 0)
        {
            return NotFound($"No candles found for symbol '{symbol}' in selected range.");
        }

        var timeRanges = ResolveLtfTimeRanges(isLongTimeframe, "with-1w");
        var anchorRanges = AnchorRangesWithWeek;

        var turningPointsByTimestamp = TurningPointDetector
            .DetectByWindow(loadedRange, extremaWindowDays)
            .Where(x => x.Timestamp >= scanStart && x.Timestamp <= scanEnd)
            .ToDictionary(x => x.Timestamp, x => x);

        var coreCandles = loadedRange
            .Where(x => x.Timestamp >= scanStart && x.Timestamp <= scanEnd)
            .OrderBy(x => x.Timestamp)
            .ToList();

        var entries = new List<TurningPointNeighborhoodEntryDto>(coreCandles.Count);
        foreach (var candle in coreCandles)
        {
            var stats = new List<TurningPointStatsDto>(timeRanges.Length);
            foreach (var timeRange in timeRanges)
            {
                var osci = await bitcoinStatisticsService.GetStatistics(candle.Timestamp, true, timeRange, ct);
                var ma = await bitcoinStatisticsService.GetStatistics(candle.Timestamp, false, timeRange, ct);
                var osciStats = osci.TimeRangeStatistics.Single();
                var maStats = ma.TimeRangeStatistics.Single();

                stats.Add(new TurningPointStatsDto(
                    timeRange,
                    osciStats.IndicatorBuyCount,
                    osciStats.IndicatorSellCount,
                    osciStats.IndicatorStrongTrendCount,
                    osciStats.IndicatorMediumTrendCount,
                    maStats.IndicatorBuyCount,
                    maStats.IndicatorSellCount));
            }

            var bullish = EvaluateDirectionalConfluenceMetrics(stats, isBullish: true, minTrendIndicators, anchorRanges, maxConflictingAnchorRanges);
            var bearish = EvaluateDirectionalConfluenceMetrics(stats, isBullish: false, minTrendIndicators, anchorRanges, maxConflictingAnchorRanges);

            turningPointsByTimestamp.TryGetValue(candle.Timestamp, out var turningPoint);
            entries.Add(new TurningPointNeighborhoodEntryDto(
                candle.Timestamp,
                candle.ClosePrice,
                turningPoint is not null,
                turningPoint?.Kind.ToString(),
                turningPoint is null
                    ? null
                    : turningPoint.Kind == TurningPointKind.LocalLow ? "Buy" : "Sell",
                bullish,
                bearish,
                bullish.PassesRule,
                bearish.PassesRule,
                stats));
        }

        return Ok(new TurningPointNeighborhoodAnalysisDto(
            center,
            windowDays,
            minTrendIndicators,
            symbol,
            isLongTimeframe,
            entries));
    }

    /// <summary>
    /// Audits local highs/lows and checks whether a real Buy/Sell action log match exists
    /// within both a day-distance window and price tolerance.
    ///
    /// LocalLow  -> expected Buy (signal must be &lt;= low * (1 + tolerance%))
    /// LocalHigh -> expected Sell (signal must be &gt;= high * (1 - tolerance%))
    /// </summary>
    [HttpGet("turning-points/match-audit")]
    public async Task<ActionResult<TurningPointMatchAuditDto>> GetTurningPointMatchAudit(
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] string symbol = "BTC1D",
        [FromQuery] bool isLongTimeframe = true,
        [FromQuery] double priceTolerancePercent = 5.0,
        [FromQuery] int maxMatchDays = 15,
        [FromQuery] int extremaWindowDays = TurningPointDetector.DefaultWindowDays,
        CancellationToken ct = default)
    {
        if (priceTolerancePercent < 0 || priceTolerancePercent > 50)
        {
            return BadRequest("priceTolerancePercent must be between 0 and 50.");
        }

        if (maxMatchDays < 0 || maxMatchDays > 60)
        {
            return BadRequest("maxMatchDays must be between 0 and 60.");
        }

        var extremaWindowValidationError = ValidateExtremaWindowDays(extremaWindowDays);
        if (extremaWindowValidationError is not null)
        {
            return extremaWindowValidationError;
        }

        var end = (endUtc ?? DateTime.UtcNow).ToUniversalTime();
        var start = (startUtc ?? new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToUniversalTime();
        if (end <= start)
        {
            return BadRequest("endUtc must be greater than startUtc.");
        }

        var contextPadding = TimeSpan.FromDays(extremaWindowDays);
        var candles = await stockCandleRepository.GetStockCandlesUpToAsync(symbol, end + contextPadding, ct);
        var allCandles = candles
            .Where(x => x.Timestamp >= start - contextPadding && x.Timestamp <= end + contextPadding)
            .OrderBy(x => x.Timestamp)
            .ToList();

        if (allCandles.Count == 0)
        {
            return NotFound($"No candles found for symbol '{symbol}' in selected range.");
        }

        var turningPoints = TurningPointDetector
            .DetectByWindow(allCandles, extremaWindowDays)
            .Where(x => x.Timestamp >= start && x.Timestamp <= end)
            .OrderBy(x => x.Timestamp)
            .ToList();

        var buys = await buyActionLogRepository.GetBuyActionLogs(start, end, isLongTimeframe, ct);
        var sells = await sellActionLogRepository.GetSellActionLogs(start, end, isLongTimeframe, ct);

        var items = new List<TurningPointMatchAuditItemDto>(turningPoints.Count);
        foreach (var point in turningPoints)
        {
            if (point.Kind == TurningPointKind.LocalLow)
            {
                var matches = buys
                    .Where(x => IsWithinDayDistance(x.TradingDate.ToUniversalTime().Date, point.Timestamp.Date, maxMatchDays))
                    .Where(x => IsBuyPriceWithinTolerance(x.Price, point.ClosePrice, priceTolerancePercent))
                    .OrderBy(x => (x.TradingDate.ToUniversalTime().Date - point.Timestamp.Date).Duration())
                    .ThenBy(x => Math.Abs((double)(x.Price - point.ClosePrice)))
                    .ToList();

                var best = matches.FirstOrDefault();
                items.Add(BuildTurningPointAuditItem(point, "Buy", best?.TradingDate.ToUniversalTime(), best?.Price, best?.TradeAdvice));
                continue;
            }

            var sellMatches = sells
                .Where(x => IsWithinDayDistance(x.TradingDate.ToUniversalTime().Date, point.Timestamp.Date, maxMatchDays))
                .Where(x => IsSellPriceWithinTolerance(x.Price, point.ClosePrice, priceTolerancePercent))
                .OrderBy(x => (x.TradingDate.ToUniversalTime().Date - point.Timestamp.Date).Duration())
                .ThenBy(x => Math.Abs((double)(x.Price - point.ClosePrice)))
                .ToList();

            var sellBest = sellMatches.FirstOrDefault();
            items.Add(BuildTurningPointAuditItem(point, "Sell", sellBest?.TradingDate.ToUniversalTime(), sellBest?.Price, sellBest?.TradeAdvice));
        }

        var matchedCount = items.Count(x => x.IsMatched);
        return Ok(new TurningPointMatchAuditDto(
            start,
            end,
            symbol,
            isLongTimeframe,
            Math.Round(priceTolerancePercent, 2),
            maxMatchDays,
            turningPoints.Count,
            matchedCount,
            turningPoints.Count - matchedCount,
            items));
    }

    // ─── Condition Proposal ──────────────────────────────────────────────────

    public record ConditionThresholdDto(
        int MinAlignedRanges,
        int MinAlignedAnchorRanges,
        int MinTrendCountSum,
        int ExtremaCoveredCount,
        int TotalExtrema,
        double CoveragePercent,
        string Direction
    );

    public record ConditionProposalDto(
        DateTime StartUtc,
        DateTime EndUtc,
        int WindowDays,
        int TotalExtrema,
        IReadOnlyList<ConditionThresholdDto> BullishThresholds,
        IReadOnlyList<ConditionThresholdDto> BearishThresholds
    );

    /// <summary>
    /// Analyses all local extrema in the date range and sweeps a grid of threshold combinations
    /// (minAlignedRanges × minAlignedAnchorRanges × minTrendCountSum). For each combination,
    /// it reports how many extrema have at least one candidate day within ±windowDays that passes
    /// the threshold. Returns results sorted by coverage descending.
    ///
    /// Use this to pick a concrete threshold for LongTimeframeMultiTrendBullishConditionMet /
    /// LongTimeframeMultiTrendBearishConditionMet.
    /// </summary>
    [HttpGet("turning-points/condition-proposal")]
    public async Task<ActionResult<ConditionProposalDto>> GetConditionProposal(
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] int windowDays = 7,
        [FromQuery] int extremaWindowDays = TurningPointDetector.DefaultWindowDays,
        [FromQuery] int maxConflictingAnchorRanges = 0,
        [FromQuery] string symbol = "BTC1D",
        [FromQuery] bool isLongTimeframe = true,
        [FromQuery] string ltfProfile = "with-1w",
        CancellationToken ct = default)
    {
        if (windowDays < 1 || windowDays > 30)
        {
            return BadRequest("windowDays must be between 1 and 30.");
        }

        var extremaWindowValidationError = ValidateExtremaWindowDays(extremaWindowDays);
        if (extremaWindowValidationError is not null)
        {
            return extremaWindowValidationError;
        }

        var maxConflictingAnchorRangesValidationError = ValidateMaxConflictingAnchorRanges(maxConflictingAnchorRanges);
        if (maxConflictingAnchorRangesValidationError is not null)
        {
            return maxConflictingAnchorRangesValidationError;
        }

        var ltfProfileValidationError = ValidateLtfProfile(ltfProfile);
        if (ltfProfileValidationError is not null)
        {
            return ltfProfileValidationError;
        }

        var end = (endUtc ?? DateTime.UtcNow).ToUniversalTime();
        var start = (startUtc ?? new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToUniversalTime();
        if (end <= start)
        {
            return BadRequest("endUtc must be greater than startUtc.");
        }

        var contextPadding = TimeSpan.FromDays(extremaWindowDays);
        var candles = await stockCandleRepository.GetStockCandlesUpToAsync(symbol, end + contextPadding, ct);
        var allCandles = candles
            .Where(x => x.Timestamp >= start - contextPadding && x.Timestamp <= end + contextPadding)
            .OrderBy(x => x.Timestamp)
            .ToList();

        if (allCandles.Count == 0)
        {
            return NotFound($"No candles found for symbol '{symbol}' in selected range.");
        }

        var turningPoints = TurningPointDetector
            .DetectByWindow(allCandles, extremaWindowDays)
            .Where(x => x.Timestamp >= start && x.Timestamp <= end)
            .OrderBy(x => x.Timestamp)
            .ToList();

        if (turningPoints.Count == 0)
        {
            return NotFound("No turning points found in the selected range.");
        }

        var timeRanges = ResolveLtfTimeRanges(isLongTimeframe, ltfProfile);
        var anchorRanges = ResolveLtfAnchorRanges(isLongTimeframe, ltfProfile);

        var window = TimeSpan.FromDays(windowDays);

        // Pre-compute per-day stats across the full range to avoid redundant API calls.
        // We need stats for all days within ±windowDays of any extremum.
        var daysToLoad = turningPoints
            .SelectMany(tp => Enumerable
                .Range(-windowDays, windowDays * 2 + 1)
                .Select(offset => tp.Timestamp.Date.AddDays(offset)))
            .Distinct()
            .Where(d => d >= start.Date && d <= end.Date)
            .OrderBy(d => d)
            .ToList();

        var dayStats = new Dictionary<DateTime, IReadOnlyList<TurningPointStatsDto>>(daysToLoad.Count);
        foreach (var day in daysToLoad)
        {
            var dayTime = new DateTime(day.Year, day.Month, day.Day, 23, 59, 59, DateTimeKind.Utc);
            var stats = new List<TurningPointStatsDto>(timeRanges.Length);
            foreach (var timeRange in timeRanges)
            {
                var osci = await bitcoinStatisticsService.GetStatistics(dayTime, true, timeRange, ct);
                var ma = await bitcoinStatisticsService.GetStatistics(dayTime, false, timeRange, ct);
                var osciStats = osci.TimeRangeStatistics.Single();
                var maStats = ma.TimeRangeStatistics.Single();

                stats.Add(new TurningPointStatsDto(
                    timeRange,
                    osciStats.IndicatorBuyCount,
                    osciStats.IndicatorSellCount,
                    osciStats.IndicatorStrongTrendCount,
                    osciStats.IndicatorMediumTrendCount,
                    maStats.IndicatorBuyCount,
                    maStats.IndicatorSellCount));
            }

            dayStats[day] = stats;
        }

        // Sweep threshold grid.
        var bullishResults = new List<ConditionThresholdDto>();
        var bearishResults = new List<ConditionThresholdDto>();

        foreach (var minAligned in new[] { 2, 3, 4, 5 })
        {
            foreach (var minAnchor in new[] { 0, 1, 2 })
            {
                foreach (var minTrend in new[] { 0, 1, 2, 3, 4, 5, 6 })
                {
                    var bullishCovered = 0;
                    var bearishCovered = 0;

                    foreach (var tp in turningPoints)
                    {
                        var windowDates = dayStats.Keys
                            .Where(d => (d - tp.Timestamp.Date).Duration() <= window)
                            .ToList();

                        var hasBullishCandidate = windowDates.Any(d =>
                        {
                            var metrics = EvaluateDirectionalConfluenceMetricsWithThreshold(
                                dayStats[d], isBullish: true, minAligned, minAnchor, minTrend, anchorRanges, maxConflictingAnchorRanges);
                            return metrics.PassesRule;
                        });

                        var hasBearishCandidate = windowDates.Any(d =>
                        {
                            var metrics = EvaluateDirectionalConfluenceMetricsWithThreshold(
                                dayStats[d], isBullish: false, minAligned, minAnchor, minTrend, anchorRanges, maxConflictingAnchorRanges);
                            return metrics.PassesRule;
                        });

                        if (tp.Kind == TurningPointKind.LocalLow && hasBullishCandidate)
                        {
                            bullishCovered++;
                        }

                        if (tp.Kind == TurningPointKind.LocalHigh && hasBearishCandidate)
                        {
                            bearishCovered++;
                        }
                    }

                    var localLowCount = turningPoints.Count(x => x.Kind == TurningPointKind.LocalLow);
                    var localHighCount = turningPoints.Count(x => x.Kind == TurningPointKind.LocalHigh);

                    bullishResults.Add(new ConditionThresholdDto(
                        minAligned, minAnchor, minTrend,
                        bullishCovered, localLowCount,
                        localLowCount > 0 ? Math.Round(100.0 * bullishCovered / localLowCount, 1) : 0,
                        "Bullish"));

                    bearishResults.Add(new ConditionThresholdDto(
                        minAligned, minAnchor, minTrend,
                        bearishCovered, localHighCount,
                        localHighCount > 0 ? Math.Round(100.0 * bearishCovered / localHighCount, 1) : 0,
                        "Bearish"));
                }
            }
        }

        return Ok(new ConditionProposalDto(
            start,
            end,
            windowDays,
            turningPoints.Count,
            bullishResults
                .OrderByDescending(x => x.CoveragePercent)
                .ThenByDescending(x => x.MinAlignedRanges + x.MinAlignedAnchorRanges + x.MinTrendCountSum)
                .ToList(),
            bearishResults
                .OrderByDescending(x => x.CoveragePercent)
                .ThenByDescending(x => x.MinAlignedRanges + x.MinAlignedAnchorRanges + x.MinTrendCountSum)
                .ToList()));
    }

    private static ConfluenceDirectionMetricsDto EvaluateDirectionalConfluenceMetricsWithThreshold(
        IReadOnlyCollection<TurningPointStatsDto> stats,
        bool isBullish,
        int minAlignedRanges,
        int minAlignedAnchorRanges,
        int minTrendCountSum,
        IReadOnlySet<string> anchorRanges,
        int maxConflictingAnchorRanges)
    {
        var alignedRanges = 0;
        var alignedAnchorRanges = 0;
        var conflictingAnchorRanges = 0;
        var trendCountSum = 0;

        foreach (var entry in stats)
        {
            var trendCount = entry.OscillatorStrongTrendCount + entry.OscillatorMediumTrendCount;
            trendCountSum += trendCount;

            // Oscillator-only alignment: ignore moving averages.
            // This correctly identifies bear-market bottoms where oscillators align bullish
            // but MAs remain structurally below price.
            var isAligned = isBullish
                ? entry.OscillatorBuyCount > entry.OscillatorSellCount
                : entry.OscillatorSellCount > entry.OscillatorBuyCount;

            var isConflicting = isBullish
                ? entry.OscillatorSellCount > entry.OscillatorBuyCount && trendCount > 0
                : entry.OscillatorBuyCount > entry.OscillatorSellCount && trendCount > 0;

            if (isAligned)
            {
                alignedRanges++;
                if (anchorRanges.Contains(entry.TimeRange))
                {
                    alignedAnchorRanges++;
                }
            }

            if (isConflicting && anchorRanges.Contains(entry.TimeRange))
            {
                conflictingAnchorRanges++;
            }
        }

        var passesRule = alignedRanges >= minAlignedRanges
            && alignedAnchorRanges >= minAlignedAnchorRanges
            && conflictingAnchorRanges <= maxConflictingAnchorRanges
            && trendCountSum >= minTrendCountSum;

        return new ConfluenceDirectionMetricsDto(
            alignedRanges,
            alignedAnchorRanges,
            conflictingAnchorRanges,
            trendCountSum,
            trendCountSum >= minTrendCountSum,
            passesRule);
    }

    // ─── Error Rates ─────────────────────────────────────────────────────────

    public record ErrorRateThresholdDto(
        int MinAlignedRanges,
        int MinAlignedAnchorRanges,
        int MinTrendCountSum,
        int TotalSignalDays,
        double SignalsPerYear,
        int TypeIErrorCount,
        double TypeIErrorPercent,
        int TotalTurningPoints,
        int TypeIIErrorCount,
        double TypeIIErrorPercent,
        bool BothUnder5Percent,
        bool MeetsConstraints,
        double StabilityScore,
        double ConstrainedScore,
        string Direction
    );

    public record ErrorRateConstraintsDto(
        double MaxTypeIIErrorPercent,
        double MinSignalDaysPerYear,
        int TopK
    );

    public record ErrorRateCandidateDto(
        string Direction,
        int MinAlignedRanges,
        int MinAlignedAnchorRanges,
        int MinTrendCountSum,
        double TypeIErrorPercent,
        double TypeIIErrorPercent,
        double SignalsPerYear,
        double StabilityScore,
        double ConstrainedScore,
        bool MeetsConstraints
    );

    public record ErrorRateDataQualityDto(
        int TotalDaysInRange,
        int DaysWithCompleteStats,
        int DaysWithIncompleteStats,
        double CompleteStatsCoveragePercent,
        IReadOnlyDictionary<string, int> MissingStatsDayCountByTimeRange
    );

    public record ErrorRateProposalDto(
        DateTime StartUtc,
        DateTime EndUtc,
        double PriceTolerancePercent,
        int MaxMatchDays,
        int TotalDaysEvaluated,
        ErrorRateDataQualityDto DataQuality,
        ErrorRateConstraintsDto Constraints,
        int TotalTurningPoints,
        IReadOnlyList<ErrorRateThresholdDto> BullishThresholds,
        IReadOnlyList<ErrorRateThresholdDto> BearishThresholds,
        IReadOnlyList<ErrorRateCandidateDto> TopBullishCandidates,
        IReadOnlyList<ErrorRateCandidateDto> TopBearishCandidates
    );

    public record ErrorRateFrontierPointDto(
        double MaxTypeIIErrorPercent,
        double MinSignalDaysPerYear,
        int BullishFeasibleCount,
        int BearishFeasibleCount,
        IReadOnlyList<ErrorRateCandidateDto> TopBullishCandidates,
        IReadOnlyList<ErrorRateCandidateDto> TopBearishCandidates
    );

    public record ErrorRateFrontierDto(
        DateTime StartUtc,
        DateTime EndUtc,
        double PriceTolerancePercent,
        int MaxMatchDays,
        int TotalDaysEvaluated,
        ErrorRateDataQualityDto DataQuality,
        int TotalTurningPoints,
        IReadOnlyList<ErrorRateFrontierPointDto> Points
    );

    public record SignalFollowThroughThresholdDto(
        double MinimumMovePercent,
        int TotalSignalDays,
        double SignalsPerYear,
        int TypeIErrorCount,
        double TypeIErrorPercent,
        int TotalTurningPoints,
        int TypeIIErrorCount,
        double TypeIIErrorPercent,
        bool MeetsConstraints,
        double StabilityScore,
        double ConstrainedScore,
        string Direction
    );

    public record SignalFollowThroughCandidateDto(
        string Direction,
        double MinimumMovePercent,
        double TypeIErrorPercent,
        double TypeIIErrorPercent,
        double SignalsPerYear,
        double StabilityScore,
        double ConstrainedScore,
        bool MeetsConstraints
    );

    public record SignalFollowThroughProposalDto(
        DateTime StartUtc,
        DateTime EndUtc,
        double PriceTolerancePercent,
        int MaxMatchDays,
        int TotalDaysEvaluated,
        ErrorRateDataQualityDto DataQuality,
        ErrorRateConstraintsDto Constraints,
        int TotalTurningPoints,
        double CurrentBullishMinimumMovePercent,
        double CurrentBearishMinimumMovePercent,
        IReadOnlyList<double> EvaluatedMinimumMovePercents,
        IReadOnlyList<SignalFollowThroughThresholdDto> BullishThresholds,
        IReadOnlyList<SignalFollowThroughThresholdDto> BearishThresholds,
        IReadOnlyList<SignalFollowThroughCandidateDto> TopBullishCandidates,
        IReadOnlyList<SignalFollowThroughCandidateDto> TopBearishCandidates
    );

    /// <summary>
    /// Sweeps a threshold grid and for each combination reports both Type I and Type II error rates.
    ///
    /// TypeI  (false positive): signal fires but signal does not match any turning point
    ///                              within configured price tolerance and max day distance.
    /// TypeII (false negative): turning point exists but no signal matches it within both constraints.
    ///
    /// Results sorted by combined error rate (TypeI + TypeII) ascending.
    /// BothUnder5Percent=true marks combinations that satisfy the ≤5%/≤5% target.
    ///
    /// Note: loads indicator statistics for every day in the date range — prefer a 1–2 year window
    /// for iterative threshold tuning.
    /// </summary>
    [HttpGet("turning-points/error-rates")]
    public async Task<ActionResult<ErrorRateProposalDto>> GetErrorRates(
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] double priceTolerancePercent = 10.0,
        [FromQuery] int maxMatchDays = 15,
        [FromQuery] double maxTypeIIErrorPercent = 25.0,
        [FromQuery] double minSignalDaysPerYear = 6.0,
        [FromQuery] int topK = 10,
        [FromQuery] int extremaWindowDays = 14,
        [FromQuery] int maxConflictingAnchorRanges = 0,
        [FromQuery] int bullishMinOffsetDays = -15,
        [FromQuery] int bullishMaxOffsetDays = 15,
        [FromQuery] int bearishMinOffsetDays = -15,
        [FromQuery] int bearishMaxOffsetDays = 15,
        [FromQuery] string symbol = "BTC1D",
        [FromQuery] bool isLongTimeframe = true,
        [FromQuery] string ltfProfile = "with-1w",
        CancellationToken ct = default)
    {
        if (priceTolerancePercent < 0 || priceTolerancePercent > 50)
            return BadRequest("priceTolerancePercent must be between 0 and 50.");
        if (maxMatchDays < 0 || maxMatchDays > 60)
            return BadRequest("maxMatchDays must be between 0 and 60.");
        if (maxTypeIIErrorPercent < 0 || maxTypeIIErrorPercent > 50)
            return BadRequest("maxTypeIIErrorPercent must be between 0 and 50.");
        if (minSignalDaysPerYear < 0 || minSignalDaysPerYear > 365)
            return BadRequest("minSignalDaysPerYear must be between 0 and 365.");
        if (topK < 1 || topK > 50)
            return BadRequest("topK must be between 1 and 50.");
        var extremaWindowValidationError = ValidateExtremaWindowDays(extremaWindowDays);
        if (extremaWindowValidationError is not null)
            return extremaWindowValidationError;
        var maxConflictingAnchorRangesValidationError = ValidateMaxConflictingAnchorRanges(maxConflictingAnchorRanges);
        if (maxConflictingAnchorRangesValidationError is not null)
            return maxConflictingAnchorRangesValidationError;
        var bullishOffsetValidationError = ValidateSignalOffsetWindow("bullish", bullishMinOffsetDays, bullishMaxOffsetDays, maxMatchDays);
        if (bullishOffsetValidationError is not null)
            return bullishOffsetValidationError;
        var bearishOffsetValidationError = ValidateSignalOffsetWindow("bearish", bearishMinOffsetDays, bearishMaxOffsetDays, maxMatchDays);
        if (bearishOffsetValidationError is not null)
            return bearishOffsetValidationError;
        var ltfProfileValidationError = ValidateLtfProfile(ltfProfile);
        if (ltfProfileValidationError is not null)
            return ltfProfileValidationError;

        var end = (endUtc ?? DateTime.UtcNow.Date.AddDays(-1).AddSeconds(-1).ToUniversalTime()).ToUniversalTime();
        var start = (startUtc ?? new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToUniversalTime();
        if (end <= start)
            return BadRequest("endUtc must be greater than startUtc.");

        var contextPadding = TimeSpan.FromDays(extremaWindowDays);
        var candles = await stockCandleRepository.GetStockCandlesUpToAsync(symbol, end + contextPadding, ct);
        var allCandles = candles
            .Where(x => x.Timestamp >= start - contextPadding && x.Timestamp <= end + contextPadding)
            .OrderBy(x => x.Timestamp)
            .ToList();

        if (allCandles.Count == 0)
            return NotFound($"No candles found for symbol '{symbol}' in selected range.");

        var turningPoints = TurningPointDetector
            .DetectByWindow(allCandles, extremaWindowDays)
            .Where(x => x.Timestamp >= start && x.Timestamp <= end)
            .OrderBy(x => x.Timestamp)
            .ToList();

        var timeRanges = ResolveLtfTimeRanges(isLongTimeframe, ltfProfile);
        var anchorRanges = ResolveLtfAnchorRanges(isLongTimeframe, ltfProfile);

        // Load indicator stats for every day in range (required for TypeI false-positive counting).
        var inRangeDailyClose = allCandles
            .Where(x => x.Timestamp >= start && x.Timestamp <= end)
            .GroupBy(x => x.Timestamp.Date)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.Timestamp).Last().ClosePrice);

        var inRangeDays = inRangeDailyClose.Keys
            .OrderBy(d => d)
            .ToList();

        var allDayStats = new Dictionary<DateTime, IReadOnlyList<TurningPointStatsDto>>(inRangeDays.Count);
        var missingStatsByTimeRange = timeRanges.ToDictionary(x => x, _ => 0);
        foreach (var day in inRangeDays)
        {
            var dayTime = new DateTime(day.Year, day.Month, day.Day, 23, 59, 59, DateTimeKind.Utc);
            var stats = new List<TurningPointStatsDto>(timeRanges.Length);
            var dataComplete = true;
            foreach (var timeRange in timeRanges)
            {
                try
                {
                    var osci = await bitcoinStatisticsService.GetStatistics(dayTime, true, timeRange, ct);
                    var ma = await bitcoinStatisticsService.GetStatistics(dayTime, false, timeRange, ct);
                    var osciStats = osci.TimeRangeStatistics.Single();
                    var maStats = ma.TimeRangeStatistics.Single();
                    stats.Add(new TurningPointStatsDto(
                        timeRange,
                        osciStats.IndicatorBuyCount,
                        osciStats.IndicatorSellCount,
                        osciStats.IndicatorStrongTrendCount,
                        osciStats.IndicatorMediumTrendCount,
                        maStats.IndicatorBuyCount,
                        maStats.IndicatorSellCount));
                }
                catch (InvalidDataException)
                {
                    dataComplete = false;
                    missingStatsByTimeRange[timeRange]++;
                    break;
                }
            }

            if (dataComplete)
            {
                allDayStats[day] = stats;
            }
        }

        var localLows = turningPoints
            .Where(x => x.Kind == TurningPointKind.LocalLow)
            .ToList();
        var localHighs = turningPoints
            .Where(x => x.Kind == TurningPointKind.LocalHigh)
            .ToList();

        var localLowCount = localLows.Count;
        var localHighCount = localHighs.Count;

        // Sweep threshold grid.
        var bullishResults = new List<ErrorRateThresholdDto>();
        var bearishResults = new List<ErrorRateThresholdDto>();
        var completeDays = allDayStats.Keys.OrderBy(d => d).ToList();
        var daysWithCompleteStats = completeDays.Count;
        var totalDaysInRange = inRangeDays.Count;
        var daysWithIncompleteStats = totalDaysInRange - daysWithCompleteStats;
        var completeStatsCoveragePercent = totalDaysInRange > 0
            ? Math.Round(100.0 * daysWithCompleteStats / totalDaysInRange, 1)
            : 0.0;
        var yearsInSample = Math.Max((end - start).TotalDays / 365.25, 0.1);
        var completeDailyClose = inRangeDailyClose
            .Where(kvp => allDayStats.ContainsKey(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        foreach (var minAligned in new[] { 2, 3, 4, 5 })
        {
            foreach (var minAnchor in new[] { 0, 1, 2 })
            {
                foreach (var minTrend in new[] { 0, 1, 2, 3, 4, 5, 6 })
                {
                    var bullishSignalDays = new List<DateTime>();
                    var bearishSignalDays = new List<DateTime>();

                    foreach (var day in completeDays)
                    {
                        var bMetrics = EvaluateDirectionalConfluenceMetricsWithThreshold(
                            allDayStats[day], isBullish: true, minAligned, minAnchor, minTrend, anchorRanges, maxConflictingAnchorRanges);
                        if (bMetrics.PassesRule) bullishSignalDays.Add(day);

                        var sMetrics = EvaluateDirectionalConfluenceMetricsWithThreshold(
                            allDayStats[day], isBullish: false, minAligned, minAnchor, minTrend, anchorRanges, maxConflictingAnchorRanges);
                        if (sMetrics.PassesRule) bearishSignalDays.Add(day);
                    }

                    // TypeI: signal fires but price is not within tolerance of any matching turning point.
                    var bullishTypeI = bullishSignalDays
                        .Count(d => !localLows.Any(low =>
                            IsBullishSignalMatch(
                                d,
                                completeDailyClose[d],
                                low.Timestamp.Date,
                                low.ClosePrice,
                                priceTolerancePercent,
                                maxMatchDays,
                                bullishMinOffsetDays,
                                bullishMaxOffsetDays)));
                    var bearishTypeI = bearishSignalDays
                        .Count(d => !localHighs.Any(high =>
                            IsBearishSignalMatch(
                                d,
                                completeDailyClose[d],
                                high.Timestamp.Date,
                                high.ClosePrice,
                                priceTolerancePercent,
                                maxMatchDays,
                                bearishMinOffsetDays,
                                bearishMaxOffsetDays)));

                    // TypeII: turning point exists but no signal price matches it within tolerance.
                    var bullishTypeII = localLows
                        .Count(low => !bullishSignalDays.Any(d =>
                            IsBullishSignalMatch(
                                d,
                                completeDailyClose[d],
                                low.Timestamp.Date,
                                low.ClosePrice,
                                priceTolerancePercent,
                                maxMatchDays,
                                bullishMinOffsetDays,
                                bullishMaxOffsetDays)));
                    var bearishTypeII = localHighs
                        .Count(high => !bearishSignalDays.Any(d =>
                            IsBearishSignalMatch(
                                d,
                                completeDailyClose[d],
                                high.Timestamp.Date,
                                high.ClosePrice,
                                priceTolerancePercent,
                                maxMatchDays,
                                bearishMinOffsetDays,
                                bearishMaxOffsetDays)));

                    var bullishTypeIPercent = bullishSignalDays.Count > 0
                        ? Math.Round(100.0 * bullishTypeI / bullishSignalDays.Count, 1) : 0.0;
                    var bullishTypeIIPercent = localLowCount > 0
                        ? Math.Round(100.0 * bullishTypeII / localLowCount, 1) : 0.0;
                    var bearishTypeIPercent = bearishSignalDays.Count > 0
                        ? Math.Round(100.0 * bearishTypeI / bearishSignalDays.Count, 1) : 0.0;
                    var bearishTypeIIPercent = localHighCount > 0
                        ? Math.Round(100.0 * bearishTypeII / localHighCount, 1) : 0.0;
                    var bullishSignalsPerYear = Math.Round(bullishSignalDays.Count / yearsInSample, 1);
                    var bearishSignalsPerYear = Math.Round(bearishSignalDays.Count / yearsInSample, 1);

                    var bullishStabilityScore = CalculateStabilityScoreByYear(
                        bullishSignalDays,
                        localLows,
                        completeDailyClose,
                        isBullish: true,
                        priceTolerancePercent,
                        maxMatchDays,
                        bullishMinOffsetDays,
                        bullishMaxOffsetDays);
                    var bearishStabilityScore = CalculateStabilityScoreByYear(
                        bearishSignalDays,
                        localHighs,
                        completeDailyClose,
                        isBullish: false,
                        priceTolerancePercent,
                        maxMatchDays,
                        bearishMinOffsetDays,
                        bearishMaxOffsetDays);

                    var bullishMeetsConstraints = bullishTypeIIPercent <= maxTypeIIErrorPercent
                        && bullishSignalsPerYear >= minSignalDaysPerYear
                        && bullishSignalDays.Count > 0;
                    var bearishMeetsConstraints = bearishTypeIIPercent <= maxTypeIIErrorPercent
                        && bearishSignalsPerYear >= minSignalDaysPerYear
                        && bearishSignalDays.Count > 0;

                    var bullishConstrainedScore = CalculateConstrainedScore(
                        bullishTypeIPercent,
                        bullishTypeIIPercent,
                        bullishSignalsPerYear,
                        bullishStabilityScore,
                        maxTypeIIErrorPercent,
                        minSignalDaysPerYear);
                    var bearishConstrainedScore = CalculateConstrainedScore(
                        bearishTypeIPercent,
                        bearishTypeIIPercent,
                        bearishSignalsPerYear,
                        bearishStabilityScore,
                        maxTypeIIErrorPercent,
                        minSignalDaysPerYear);

                    bullishResults.Add(new ErrorRateThresholdDto(
                        minAligned, minAnchor, minTrend,
                        bullishSignalDays.Count,
                        bullishSignalsPerYear,
                        bullishTypeI, bullishTypeIPercent,
                        localLowCount,
                        bullishTypeII, bullishTypeIIPercent,
                        bullishTypeIPercent <= 5.0 && bullishTypeIIPercent <= 5.0,
                        bullishMeetsConstraints,
                        bullishStabilityScore,
                        bullishConstrainedScore,
                        "Bullish"));

                    bearishResults.Add(new ErrorRateThresholdDto(
                        minAligned, minAnchor, minTrend,
                        bearishSignalDays.Count,
                        bearishSignalsPerYear,
                        bearishTypeI, bearishTypeIPercent,
                        localHighCount,
                        bearishTypeII, bearishTypeIIPercent,
                        bearishTypeIPercent <= 5.0 && bearishTypeIIPercent <= 5.0,
                        bearishMeetsConstraints,
                        bearishStabilityScore,
                        bearishConstrainedScore,
                        "Bearish"));
                }
            }
        }

        var sortedBullish = bullishResults
            .OrderBy(x => x.ConstrainedScore)
            .ThenBy(x => x.TypeIErrorPercent)
            .ThenByDescending(x => x.StabilityScore)
            .ToList();

        var sortedBearish = bearishResults
            .OrderBy(x => x.ConstrainedScore)
            .ThenBy(x => x.TypeIErrorPercent)
            .ThenByDescending(x => x.StabilityScore)
            .ToList();

        return Ok(new ErrorRateProposalDto(
            start,
            end,
            Math.Round(priceTolerancePercent, 2),
            maxMatchDays,
            inRangeDays.Count,
            new ErrorRateDataQualityDto(
                totalDaysInRange,
                daysWithCompleteStats,
                daysWithIncompleteStats,
                completeStatsCoveragePercent,
                new Dictionary<string, int>(missingStatsByTimeRange)),
            new ErrorRateConstraintsDto(
                Math.Round(maxTypeIIErrorPercent, 2),
                Math.Round(minSignalDaysPerYear, 2),
                topK),
            turningPoints.Count,
            sortedBullish,
            sortedBearish,
            sortedBullish
                .Take(topK)
                .Select(x => new ErrorRateCandidateDto(
                    x.Direction,
                    x.MinAlignedRanges,
                    x.MinAlignedAnchorRanges,
                    x.MinTrendCountSum,
                    x.TypeIErrorPercent,
                    x.TypeIIErrorPercent,
                    x.SignalsPerYear,
                    x.StabilityScore,
                    x.ConstrainedScore,
                    x.MeetsConstraints))
                .ToList(),
            sortedBearish
                .Take(topK)
                .Select(x => new ErrorRateCandidateDto(
                    x.Direction,
                    x.MinAlignedRanges,
                    x.MinAlignedAnchorRanges,
                    x.MinTrendCountSum,
                    x.TypeIErrorPercent,
                    x.TypeIIErrorPercent,
                    x.SignalsPerYear,
                    x.StabilityScore,
                    x.ConstrainedScore,
                    x.MeetsConstraints))
                .ToList()));
    }

    /// <summary>
    /// Runs one base error-rate sweep and projects a frontier over multiple constraint profiles.
    /// This avoids reloading BIS data for each profile and re-scores thresholds in-memory.
    /// </summary>
    [HttpGet("turning-points/error-rates/frontier")]
    public async Task<ActionResult<ErrorRateFrontierDto>> GetErrorRatesFrontier(
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] double priceTolerancePercent = 10.0,
        [FromQuery] int maxMatchDays = 15,
        [FromQuery] string maxTypeIIGrid = "10,15,20,25",
        [FromQuery] string minSignalDaysPerYearGrid = "3,6,10",
        [FromQuery] int topK = 5,
        [FromQuery] int extremaWindowDays = 14,
        [FromQuery] int maxConflictingAnchorRanges = 0,
        [FromQuery] int bullishMinOffsetDays = -15,
        [FromQuery] int bullishMaxOffsetDays = 15,
        [FromQuery] int bearishMinOffsetDays = -15,
        [FromQuery] int bearishMaxOffsetDays = 15,
        [FromQuery] string symbol = "BTC1D",
        [FromQuery] bool isLongTimeframe = true,
        [FromQuery] string ltfProfile = "with-1w",
        CancellationToken ct = default)
    {
        if (topK < 1 || topK > 50)
        {
            return BadRequest("topK must be between 1 and 50.");
        }

        var typeIIGrid = ParseDoubleGrid(maxTypeIIGrid, min: 0, max: 50);
        if (typeIIGrid is null || typeIIGrid.Count == 0)
        {
            return BadRequest("maxTypeIIGrid must be a comma-separated list of numbers in [0..50].");
        }

        var signalGrid = ParseDoubleGrid(minSignalDaysPerYearGrid, min: 0, max: 365);
        if (signalGrid is null || signalGrid.Count == 0)
        {
            return BadRequest("minSignalDaysPerYearGrid must be a comma-separated list of numbers in [0..365].");
        }

        var maxConflictingAnchorRangesValidationError = ValidateMaxConflictingAnchorRanges(maxConflictingAnchorRanges);
        if (maxConflictingAnchorRangesValidationError is not null)
        {
            return maxConflictingAnchorRangesValidationError;
        }

        var bullishOffsetValidationError = ValidateSignalOffsetWindow("bullish", bullishMinOffsetDays, bullishMaxOffsetDays, maxMatchDays);
        if (bullishOffsetValidationError is not null)
        {
            return bullishOffsetValidationError;
        }

        var bearishOffsetValidationError = ValidateSignalOffsetWindow("bearish", bearishMinOffsetDays, bearishMaxOffsetDays, maxMatchDays);
        if (bearishOffsetValidationError is not null)
        {
            return bearishOffsetValidationError;
        }

        var ltfProfileValidationError = ValidateLtfProfile(ltfProfile);
        if (ltfProfileValidationError is not null)
        {
            return ltfProfileValidationError;
        }

        // Build base metrics once; use relaxed constraints so no candidate is pre-filtered.
        var baseAction = await GetErrorRates(
            startUtc,
            endUtc,
            priceTolerancePercent,
            maxMatchDays,
            maxTypeIIErrorPercent: 50,
            minSignalDaysPerYear: 0,
            topK: 50,
            extremaWindowDays,
            maxConflictingAnchorRanges,
            bullishMinOffsetDays,
            bullishMaxOffsetDays,
            bearishMinOffsetDays,
            bearishMaxOffsetDays,
            symbol,
            isLongTimeframe,
            ltfProfile,
            ct);

        if (baseAction.Result is BadRequestObjectResult badRequest)
        {
            return BadRequest(badRequest.Value);
        }

        if (baseAction.Result is NotFoundObjectResult notFound)
        {
            return NotFound(notFound.Value);
        }

        var ok = baseAction.Result as OkObjectResult;
        var basePayload = ok?.Value as ErrorRateProposalDto;
        if (basePayload is null)
        {
            return StatusCode(500, "Unable to build base error-rate payload for frontier computation.");
        }

        var points = new List<ErrorRateFrontierPointDto>();
        foreach (var maxTypeII in typeIIGrid)
        {
            foreach (var minSignals in signalGrid)
            {
                var bullishCandidates = basePayload.BullishThresholds
                    .Select(x => BuildCandidateFromThreshold(x, maxTypeII, minSignals))
                    .OrderBy(x => x.ConstrainedScore)
                    .ThenBy(x => x.TypeIErrorPercent)
                    .ThenByDescending(x => x.StabilityScore)
                    .Take(topK)
                    .ToList();

                var bearishCandidates = basePayload.BearishThresholds
                    .Select(x => BuildCandidateFromThreshold(x, maxTypeII, minSignals))
                    .OrderBy(x => x.ConstrainedScore)
                    .ThenBy(x => x.TypeIErrorPercent)
                    .ThenByDescending(x => x.StabilityScore)
                    .Take(topK)
                    .ToList();

                var bullishFeasibleCount = basePayload.BullishThresholds
                    .Count(x => x.TypeIIErrorPercent <= maxTypeII && x.SignalsPerYear >= minSignals && x.TotalSignalDays > 0);
                var bearishFeasibleCount = basePayload.BearishThresholds
                    .Count(x => x.TypeIIErrorPercent <= maxTypeII && x.SignalsPerYear >= minSignals && x.TotalSignalDays > 0);

                points.Add(new ErrorRateFrontierPointDto(
                    Math.Round(maxTypeII, 2),
                    Math.Round(minSignals, 2),
                    bullishFeasibleCount,
                    bearishFeasibleCount,
                    bullishCandidates,
                    bearishCandidates));
            }
        }

        return Ok(new ErrorRateFrontierDto(
            basePayload.StartUtc,
            basePayload.EndUtc,
            basePayload.PriceTolerancePercent,
            basePayload.MaxMatchDays,
            basePayload.TotalDaysEvaluated,
            basePayload.DataQuality,
            basePayload.TotalTurningPoints,
            points
                .OrderByDescending(x => x.BullishFeasibleCount + x.BearishFeasibleCount)
                .ThenBy(x => x.TopBullishCandidates.FirstOrDefault()?.ConstrainedScore ?? double.MaxValue)
                .ThenBy(x => x.TopBearishCandidates.FirstOrDefault()?.ConstrainedScore ?? double.MaxValue)
                .ToList()));
    }

    /// <summary>
    /// Evaluates a directional price follow-through filter on top of the currently active LTF thresholds.
    /// The filter keeps the first signal in each direction and then requires subsequent same-direction
    /// signals to improve by at least the configured percentage versus the previous accepted signal.
    /// </summary>
    [HttpGet("turning-points/follow-through-sweep")]
    public async Task<ActionResult<SignalFollowThroughProposalDto>> GetSignalFollowThroughSweep(
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] double priceTolerancePercent = 10.0,
        [FromQuery] int maxMatchDays = 15,
        [FromQuery] string minMovePercentGrid = "0,0.25,0.5,0.75,1,1.25,1.5,2,2.5,3,4,5,6,7,8,10",
        [FromQuery] double maxTypeIIErrorPercent = 25.0,
        [FromQuery] double minSignalDaysPerYear = 6.0,
        [FromQuery] int topK = 10,
        [FromQuery] int extremaWindowDays = 14,
        [FromQuery] int bullishMinOffsetDays = -15,
        [FromQuery] int bullishMaxOffsetDays = 15,
        [FromQuery] int bearishMinOffsetDays = -15,
        [FromQuery] int bearishMaxOffsetDays = 15,
        [FromQuery] string symbol = "BTC1D",
        [FromQuery] bool isLongTimeframe = true,
        [FromQuery] string ltfProfile = "with-1w",
        CancellationToken ct = default)
    {
        if (priceTolerancePercent < 0 || priceTolerancePercent > 50)
            return BadRequest("priceTolerancePercent must be between 0 and 50.");
        if (maxMatchDays < 0 || maxMatchDays > 60)
            return BadRequest("maxMatchDays must be between 0 and 60.");
        if (maxTypeIIErrorPercent < 0 || maxTypeIIErrorPercent > 50)
            return BadRequest("maxTypeIIErrorPercent must be between 0 and 50.");
        if (minSignalDaysPerYear < 0 || minSignalDaysPerYear > 365)
            return BadRequest("minSignalDaysPerYear must be between 0 and 365.");
        if (topK < 1 || topK > 50)
            return BadRequest("topK must be between 1 and 50.");

        var extremaWindowValidationError = ValidateExtremaWindowDays(extremaWindowDays);
        if (extremaWindowValidationError is not null)
            return extremaWindowValidationError;

        var bullishOffsetValidationError = ValidateSignalOffsetWindow("bullish", bullishMinOffsetDays, bullishMaxOffsetDays, maxMatchDays);
        if (bullishOffsetValidationError is not null)
            return bullishOffsetValidationError;

        var bearishOffsetValidationError = ValidateSignalOffsetWindow("bearish", bearishMinOffsetDays, bearishMaxOffsetDays, maxMatchDays);
        if (bearishOffsetValidationError is not null)
            return bearishOffsetValidationError;

        var ltfProfileValidationError = ValidateLtfProfile(ltfProfile);
        if (ltfProfileValidationError is not null)
            return ltfProfileValidationError;

        var minMoveValues = ParseDoubleGrid(minMovePercentGrid, min: 0, max: 50);
        if (minMoveValues is null || minMoveValues.Count == 0)
        {
            return BadRequest("minMovePercentGrid must be a comma-separated list of numbers in [0..50].");
        }

        var end = (endUtc ?? DateTime.UtcNow.Date.AddDays(-1).AddSeconds(-1).ToUniversalTime()).ToUniversalTime();
        var start = (startUtc ?? new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToUniversalTime();
        if (end <= start)
            return BadRequest("endUtc must be greater than startUtc.");

        var contextPadding = TimeSpan.FromDays(extremaWindowDays);
        var candles = await stockCandleRepository.GetStockCandlesUpToAsync(symbol, end + contextPadding, ct);
        var allCandles = candles
            .Where(x => x.Timestamp >= start - contextPadding && x.Timestamp <= end + contextPadding)
            .OrderBy(x => x.Timestamp)
            .ToList();

        if (allCandles.Count == 0)
            return NotFound($"No candles found for symbol '{symbol}' in selected range.");

        var turningPoints = TurningPointDetector
            .DetectByWindow(allCandles, extremaWindowDays)
            .Where(x => x.Timestamp >= start && x.Timestamp <= end)
            .OrderBy(x => x.Timestamp)
            .ToList();

        var timeRanges = ResolveLtfTimeRanges(isLongTimeframe, ltfProfile);
        var anchorRanges = ResolveLtfAnchorRanges(isLongTimeframe, ltfProfile);

        var inRangeDailyClose = allCandles
            .Where(x => x.Timestamp >= start && x.Timestamp <= end)
            .GroupBy(x => x.Timestamp.Date)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.Timestamp).Last().ClosePrice);

        var inRangeDays = inRangeDailyClose.Keys
            .OrderBy(d => d)
            .ToList();

        var allDayStats = new Dictionary<DateTime, IReadOnlyList<TurningPointStatsDto>>(inRangeDays.Count);
        var missingStatsByTimeRange = timeRanges.ToDictionary(x => x, _ => 0);
        foreach (var day in inRangeDays)
        {
            var dayTime = new DateTime(day.Year, day.Month, day.Day, 23, 59, 59, DateTimeKind.Utc);
            var stats = new List<TurningPointStatsDto>(timeRanges.Length);
            var dataComplete = true;
            foreach (var timeRange in timeRanges)
            {
                try
                {
                    var osci = await bitcoinStatisticsService.GetStatistics(dayTime, true, timeRange, ct);
                    var ma = await bitcoinStatisticsService.GetStatistics(dayTime, false, timeRange, ct);
                    var osciStats = osci.TimeRangeStatistics.Single();
                    var maStats = ma.TimeRangeStatistics.Single();
                    stats.Add(new TurningPointStatsDto(
                        timeRange,
                        osciStats.IndicatorBuyCount,
                        osciStats.IndicatorSellCount,
                        osciStats.IndicatorStrongTrendCount,
                        osciStats.IndicatorMediumTrendCount,
                        maStats.IndicatorBuyCount,
                        maStats.IndicatorSellCount));
                }
                catch (InvalidDataException)
                {
                    dataComplete = false;
                    missingStatsByTimeRange[timeRange]++;
                    break;
                }
            }

            if (dataComplete)
            {
                allDayStats[day] = stats;
            }
        }

        var localLows = turningPoints.Where(x => x.Kind == TurningPointKind.LocalLow).ToList();
        var localHighs = turningPoints.Where(x => x.Kind == TurningPointKind.LocalHigh).ToList();
        var completeDays = allDayStats.Keys.OrderBy(d => d).ToList();
        var totalDaysInRange = inRangeDays.Count;
        var daysWithCompleteStats = completeDays.Count;
        var daysWithIncompleteStats = totalDaysInRange - daysWithCompleteStats;
        var completeStatsCoveragePercent = totalDaysInRange > 0
            ? Math.Round(100.0 * daysWithCompleteStats / totalDaysInRange, 1)
            : 0.0;
        var yearsInSample = Math.Max((end - start).TotalDays / 365.25, 0.1);

        var baseBullishSignals = new List<SignalFollowThroughPoint>();
        var baseBearishSignals = new List<SignalFollowThroughPoint>();

        foreach (var day in completeDays)
        {
            var bullishMetrics = EvaluateDirectionalConfluenceMetricsWithThreshold(
                allDayStats[day],
                isBullish: true,
                LongTimeframeThresholds.BullishMinAlignedRanges,
                LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
                LongTimeframeThresholds.BullishMinTrendCountSum,
                anchorRanges,
                LongTimeframeThresholds.BullishMaxConflictingAnchorRanges);
            if (bullishMetrics.PassesRule)
            {
                baseBullishSignals.Add(new SignalFollowThroughPoint(day, inRangeDailyClose[day]));
            }

            var bearishMetrics = EvaluateDirectionalConfluenceMetricsWithThreshold(
                allDayStats[day],
                isBullish: false,
                LongTimeframeThresholds.BearishMinAlignedRanges,
                LongTimeframeThresholds.BearishMinAlignedAnchorRanges,
                LongTimeframeThresholds.BearishMinTrendCountSum,
                anchorRanges,
                LongTimeframeThresholds.BearishMaxConflictingAnchorRanges);
            if (bearishMetrics.PassesRule)
            {
                baseBearishSignals.Add(new SignalFollowThroughPoint(day, inRangeDailyClose[day]));
            }
        }

        var bullishResults = new List<SignalFollowThroughThresholdDto>();
        var bearishResults = new List<SignalFollowThroughThresholdDto>();

        foreach (var minMovePercent in minMoveValues)
        {
            var filteredBullish = SignalFollowThroughFilter.FilterOrdered(baseBullishSignals, isBullish: true, minMovePercent);
            var filteredBearish = SignalFollowThroughFilter.FilterOrdered(baseBearishSignals, isBullish: false, minMovePercent);

            var bullishTypeI = filteredBullish.Count(signal => !localLows.Any(low =>
                IsBullishSignalMatch(signal.Timestamp, signal.Price, low.Timestamp.Date, low.ClosePrice, priceTolerancePercent, maxMatchDays, bullishMinOffsetDays, bullishMaxOffsetDays)));
            var bearishTypeI = filteredBearish.Count(signal => !localHighs.Any(high =>
                IsBearishSignalMatch(signal.Timestamp, signal.Price, high.Timestamp.Date, high.ClosePrice, priceTolerancePercent, maxMatchDays, bearishMinOffsetDays, bearishMaxOffsetDays)));

            var bullishTypeII = localLows.Count(low => !filteredBullish.Any(signal =>
                IsBullishSignalMatch(signal.Timestamp, signal.Price, low.Timestamp.Date, low.ClosePrice, priceTolerancePercent, maxMatchDays, bullishMinOffsetDays, bullishMaxOffsetDays)));
            var bearishTypeII = localHighs.Count(high => !filteredBearish.Any(signal =>
                IsBearishSignalMatch(signal.Timestamp, signal.Price, high.Timestamp.Date, high.ClosePrice, priceTolerancePercent, maxMatchDays, bearishMinOffsetDays, bearishMaxOffsetDays)));

            var bullishTypeIPercent = filteredBullish.Count > 0
                ? Math.Round(100.0 * bullishTypeI / filteredBullish.Count, 1)
                : 0.0;
            var bullishTypeIIPercent = localLows.Count > 0
                ? Math.Round(100.0 * bullishTypeII / localLows.Count, 1)
                : 0.0;
            var bearishTypeIPercent = filteredBearish.Count > 0
                ? Math.Round(100.0 * bearishTypeI / filteredBearish.Count, 1)
                : 0.0;
            var bearishTypeIIPercent = localHighs.Count > 0
                ? Math.Round(100.0 * bearishTypeII / localHighs.Count, 1)
                : 0.0;
            var bullishSignalsPerYear = Math.Round(filteredBullish.Count / yearsInSample, 1);
            var bearishSignalsPerYear = Math.Round(filteredBearish.Count / yearsInSample, 1);

            var bullishStabilityScore = CalculateSignalPointStabilityScoreByYear(
                filteredBullish,
                localLows,
                isBullish: true,
                priceTolerancePercent,
                maxMatchDays,
                bullishMinOffsetDays,
                bullishMaxOffsetDays);
            var bearishStabilityScore = CalculateSignalPointStabilityScoreByYear(
                filteredBearish,
                localHighs,
                isBullish: false,
                priceTolerancePercent,
                maxMatchDays,
                bearishMinOffsetDays,
                bearishMaxOffsetDays);

            var bullishMeetsConstraints = bullishTypeIIPercent <= maxTypeIIErrorPercent
                && bullishSignalsPerYear >= minSignalDaysPerYear
                && filteredBullish.Count > 0;
            var bearishMeetsConstraints = bearishTypeIIPercent <= maxTypeIIErrorPercent
                && bearishSignalsPerYear >= minSignalDaysPerYear
                && filteredBearish.Count > 0;

            var bullishConstrainedScore = CalculateConstrainedScore(
                bullishTypeIPercent,
                bullishTypeIIPercent,
                bullishSignalsPerYear,
                bullishStabilityScore,
                maxTypeIIErrorPercent,
                minSignalDaysPerYear);
            var bearishConstrainedScore = CalculateConstrainedScore(
                bearishTypeIPercent,
                bearishTypeIIPercent,
                bearishSignalsPerYear,
                bearishStabilityScore,
                maxTypeIIErrorPercent,
                minSignalDaysPerYear);

            bullishResults.Add(new SignalFollowThroughThresholdDto(
                Math.Round(minMovePercent, 2),
                filteredBullish.Count,
                bullishSignalsPerYear,
                bullishTypeI,
                bullishTypeIPercent,
                localLows.Count,
                bullishTypeII,
                bullishTypeIIPercent,
                bullishMeetsConstraints,
                bullishStabilityScore,
                bullishConstrainedScore,
                "Bullish"));

            bearishResults.Add(new SignalFollowThroughThresholdDto(
                Math.Round(minMovePercent, 2),
                filteredBearish.Count,
                bearishSignalsPerYear,
                bearishTypeI,
                bearishTypeIPercent,
                localHighs.Count,
                bearishTypeII,
                bearishTypeIIPercent,
                bearishMeetsConstraints,
                bearishStabilityScore,
                bearishConstrainedScore,
                "Bearish"));
        }

        var sortedBullish = bullishResults
            .OrderBy(x => x.ConstrainedScore)
            .ThenBy(x => x.TypeIErrorPercent)
            .ThenByDescending(x => x.StabilityScore)
            .ToList();

        var sortedBearish = bearishResults
            .OrderBy(x => x.ConstrainedScore)
            .ThenBy(x => x.TypeIErrorPercent)
            .ThenByDescending(x => x.StabilityScore)
            .ToList();

        return Ok(new SignalFollowThroughProposalDto(
            start,
            end,
            Math.Round(priceTolerancePercent, 2),
            maxMatchDays,
            inRangeDays.Count,
            new ErrorRateDataQualityDto(
                totalDaysInRange,
                daysWithCompleteStats,
                daysWithIncompleteStats,
                completeStatsCoveragePercent,
                new Dictionary<string, int>(missingStatsByTimeRange)),
            new ErrorRateConstraintsDto(
                Math.Round(maxTypeIIErrorPercent, 2),
                Math.Round(minSignalDaysPerYear, 2),
                topK),
            turningPoints.Count,
            SignalFollowThroughThresholds.BullishMinFollowThroughPercent,
            SignalFollowThroughThresholds.BearishMinFollowThroughPercent,
            minMoveValues,
            sortedBullish,
            sortedBearish,
            sortedBullish.Take(topK)
                .Select(x => new SignalFollowThroughCandidateDto(
                    x.Direction,
                    x.MinimumMovePercent,
                    x.TypeIErrorPercent,
                    x.TypeIIErrorPercent,
                    x.SignalsPerYear,
                    x.StabilityScore,
                    x.ConstrainedScore,
                    x.MeetsConstraints))
                .ToList(),
            sortedBearish.Take(topK)
                .Select(x => new SignalFollowThroughCandidateDto(
                    x.Direction,
                    x.MinimumMovePercent,
                    x.TypeIErrorPercent,
                    x.TypeIIErrorPercent,
                    x.SignalsPerYear,
                    x.StabilityScore,
                    x.ConstrainedScore,
                    x.MeetsConstraints))
                .ToList()));
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private ActionResult? ValidateExtremaWindowDays(int extremaWindowDays)
    {
        if (extremaWindowDays < TurningPointDetector.MinWindowDays || extremaWindowDays > TurningPointDetector.MaxWindowDays)
        {
            return BadRequest($"extremaWindowDays must be between {TurningPointDetector.MinWindowDays} and {TurningPointDetector.MaxWindowDays}.");
        }

        return null;
    }

    private ActionResult? ValidateMaxConflictingAnchorRanges(int maxConflictingAnchorRanges)
    {
        if (maxConflictingAnchorRanges < 0 || maxConflictingAnchorRanges > 5)
        {
            return BadRequest("maxConflictingAnchorRanges must be between 0 and 5.");
        }

        return null;
    }

    private ActionResult? ValidateSignalOffsetWindow(string direction, int minOffsetDays, int maxOffsetDays, int maxMatchDays)
    {
        if (minOffsetDays > maxOffsetDays)
        {
            return BadRequest($"{direction} offset window invalid: minOffsetDays must be <= maxOffsetDays.");
        }

        if (Math.Abs(minOffsetDays) > maxMatchDays || Math.Abs(maxOffsetDays) > maxMatchDays)
        {
            return BadRequest($"{direction} offset window must be within [-maxMatchDays, +maxMatchDays].");
        }

        return null;
    }

    private static bool HasMatchInWindow(IReadOnlyCollection<DateTime> timestamps, DateTime pivot, TimeSpan window)
    {
        return timestamps.Any(ts => (ts - pivot).Duration() <= window);
    }

    private static ErrorRateCandidateDto BuildCandidateFromThreshold(
        ErrorRateThresholdDto threshold,
        double maxTypeIIErrorPercent,
        double minSignalDaysPerYear)
    {
        var meetsConstraints = threshold.TypeIIErrorPercent <= maxTypeIIErrorPercent
            && threshold.SignalsPerYear >= minSignalDaysPerYear
            && threshold.TotalSignalDays > 0;

        var constrainedScore = CalculateConstrainedScore(
            threshold.TypeIErrorPercent,
            threshold.TypeIIErrorPercent,
            threshold.SignalsPerYear,
            threshold.StabilityScore,
            maxTypeIIErrorPercent,
            minSignalDaysPerYear);

        return new ErrorRateCandidateDto(
            threshold.Direction,
            threshold.MinAlignedRanges,
            threshold.MinAlignedAnchorRanges,
            threshold.MinTrendCountSum,
            threshold.TypeIErrorPercent,
            threshold.TypeIIErrorPercent,
            threshold.SignalsPerYear,
            threshold.StabilityScore,
            constrainedScore,
            meetsConstraints);
    }

    private static IReadOnlyList<double>? ParseDoubleGrid(string csv, double min, double max)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        var values = new List<double>();
        foreach (var token in csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(token, out var value))
            {
                return null;
            }

            if (value < min || value > max)
            {
                return null;
            }

            values.Add(Math.Round(value, 2));
        }

        return values.Distinct().OrderBy(x => x).ToList();
    }

    private static double CalculateConstrainedScore(
        double typeIErrorPercent,
        double typeIIErrorPercent,
        double signalsPerYear,
        double stabilityScore,
        double maxTypeIIErrorPercent,
        double minSignalDaysPerYear)
    {
        var baseScore = typeIErrorPercent + (0.35 * typeIIErrorPercent);
        var typeIIPenalty = Math.Max(0, typeIIErrorPercent - maxTypeIIErrorPercent) * 4.0;
        var signalRatePenalty = Math.Max(0, minSignalDaysPerYear - signalsPerYear) * 8.0;
        var stabilityPenalty = Math.Max(0, 60.0 - stabilityScore) * 0.5;

        return Math.Round(baseScore + typeIIPenalty + signalRatePenalty + stabilityPenalty, 2);
    }

    private static double CalculateStabilityScoreByYear(
        IReadOnlyCollection<DateTime> signalDays,
        IReadOnlyCollection<TurningPoint> turningPoints,
        IReadOnlyDictionary<DateTime, decimal> closeByDay,
        bool isBullish,
        double tolerancePercent,
        int maxMatchDays,
        int minOffsetDays,
        int maxOffsetDays)
    {
        var yearlyTypeIPercents = signalDays
            .GroupBy(d => d.Year)
            .Select(g =>
            {
                var yearSignals = g.ToList();
                if (yearSignals.Count == 0)
                {
                    return (double?)null;
                }

                var typeICount = yearSignals.Count(d => !turningPoints.Any(tp =>
                    tp.Timestamp.Year == g.Key
                    && (isBullish
                        ? IsBullishSignalMatch(d, closeByDay[d], tp.Timestamp.Date, tp.ClosePrice, tolerancePercent, maxMatchDays, minOffsetDays, maxOffsetDays)
                        : IsBearishSignalMatch(d, closeByDay[d], tp.Timestamp.Date, tp.ClosePrice, tolerancePercent, maxMatchDays, minOffsetDays, maxOffsetDays))));

                return Math.Round(100.0 * typeICount / yearSignals.Count, 1);
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        if (yearlyTypeIPercents.Count < 2)
        {
            return 50.0;
        }

        var mean = yearlyTypeIPercents.Average();
        var variance = yearlyTypeIPercents.Average(v => Math.Pow(v - mean, 2));
        var stdDev = Math.Sqrt(variance);
        var score = Math.Max(0.0, 100.0 - (2.0 * stdDev));

        return Math.Round(score, 1);
    }

    private static double CalculateSignalPointStabilityScoreByYear(
        IReadOnlyCollection<SignalFollowThroughPoint> signalPoints,
        IReadOnlyCollection<TurningPoint> turningPoints,
        bool isBullish,
        double tolerancePercent,
        int maxMatchDays,
        int minOffsetDays,
        int maxOffsetDays)
    {
        var yearlyTypeIPercents = signalPoints
            .GroupBy(p => p.Timestamp.Year)
            .Select(g =>
            {
                var yearSignals = g.ToList();
                if (yearSignals.Count == 0)
                {
                    return (double?)null;
                }

                var typeICount = yearSignals.Count(signal => !turningPoints.Any(tp =>
                    tp.Timestamp.Year == g.Key
                    && (isBullish
                        ? IsBullishSignalMatch(signal.Timestamp, signal.Price, tp.Timestamp.Date, tp.ClosePrice, tolerancePercent, maxMatchDays, minOffsetDays, maxOffsetDays)
                        : IsBearishSignalMatch(signal.Timestamp, signal.Price, tp.Timestamp.Date, tp.ClosePrice, tolerancePercent, maxMatchDays, minOffsetDays, maxOffsetDays))));

                return Math.Round(100.0 * typeICount / yearSignals.Count, 1);
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        if (yearlyTypeIPercents.Count < 2)
        {
            return 50.0;
        }

        var mean = yearlyTypeIPercents.Average();
        var variance = yearlyTypeIPercents.Average(v => Math.Pow(v - mean, 2));
        var stdDev = Math.Sqrt(variance);
        var score = Math.Max(0.0, 100.0 - (2.0 * stdDev));

        return Math.Round(score, 1);
    }

    private static TurningPointMatchAuditItemDto BuildTurningPointAuditItem(
        TurningPoint point,
        string expectedAdvice,
        DateTime? matchedSignalTimestampUtc,
        decimal? matchedSignalPrice,
        string? matchedSignalAdvice)
    {
        if (matchedSignalTimestampUtc is null || matchedSignalPrice is null)
        {
            return new TurningPointMatchAuditItemDto(
                point.Timestamp,
                point.ClosePrice,
                point.Kind.ToString(),
                expectedAdvice,
                false,
                null,
                null,
                null,
                null,
                null);
        }

        var dayDistance = (int)(matchedSignalTimestampUtc.Value.Date - point.Timestamp.Date).Duration().TotalDays;
        var priceDeviationPercent = point.ClosePrice == 0
            ? (double?)null
            : Math.Round(Math.Abs((double)((matchedSignalPrice.Value - point.ClosePrice) / point.ClosePrice * 100m)), 3);

        return new TurningPointMatchAuditItemDto(
            point.Timestamp,
            point.ClosePrice,
            point.Kind.ToString(),
            expectedAdvice,
            true,
            matchedSignalTimestampUtc,
            matchedSignalPrice,
            matchedSignalAdvice,
            dayDistance,
            priceDeviationPercent);
    }

    private static bool IsBuyPriceWithinTolerance(decimal signalPrice, decimal localLowPrice, double tolerancePercent)
    {
        var maxAllowedPrice = localLowPrice * (1m + (decimal)tolerancePercent / 100m);
        return signalPrice <= maxAllowedPrice;
    }

    private static bool IsSellPriceWithinTolerance(decimal signalPrice, decimal localHighPrice, double tolerancePercent)
    {
        var minAllowedPrice = localHighPrice * (1m - (decimal)tolerancePercent / 100m);
        return signalPrice >= minAllowedPrice;
    }

    private static bool IsWithinDayDistance(DateTime signalDay, DateTime turningPointDay, int maxMatchDays)
    {
        var dayDistance = (signalDay - turningPointDay).Duration().TotalDays;
        return dayDistance <= maxMatchDays;
    }

    private static bool IsBullishSignalMatch(
        DateTime signalDay,
        decimal signalPrice,
        DateTime localLowDay,
        decimal localLowPrice,
        double priceTolerancePercent,
        int maxMatchDays,
        int minOffsetDays,
        int maxOffsetDays)
    {
        return IsWithinOffsetAndDistance(signalDay, localLowDay, maxMatchDays, minOffsetDays, maxOffsetDays)
            && IsBuyPriceWithinTolerance(signalPrice, localLowPrice, priceTolerancePercent);
    }

    private static bool IsBearishSignalMatch(
        DateTime signalDay,
        decimal signalPrice,
        DateTime localHighDay,
        decimal localHighPrice,
        double priceTolerancePercent,
        int maxMatchDays,
        int minOffsetDays,
        int maxOffsetDays)
    {
        return IsWithinOffsetAndDistance(signalDay, localHighDay, maxMatchDays, minOffsetDays, maxOffsetDays)
            && IsSellPriceWithinTolerance(signalPrice, localHighPrice, priceTolerancePercent);
    }

    private static bool IsWithinOffsetAndDistance(
        DateTime signalDay,
        DateTime turningPointDay,
        int maxMatchDays,
        int minOffsetDays,
        int maxOffsetDays)
    {
        var signedDayOffset = (signalDay.Date - turningPointDay.Date).Days;
        var withinWindow = signedDayOffset >= minOffsetDays && signedDayOffset <= maxOffsetDays;
        var withinDistance = Math.Abs(signedDayOffset) <= maxMatchDays;

        return withinWindow && withinDistance;
    }

    private static readonly IReadOnlySet<string> AnchorRangesWithWeek = new HashSet<string>(StringComparer.Ordinal) { "1d", "1w" };
    private static readonly IReadOnlySet<string> AnchorRangesWithHour = new HashSet<string>(StringComparer.Ordinal) { "1d", "1h" };

    private static string[] ResolveLtfTimeRanges(bool isLongTimeframe, string ltfProfile)
    {
        if (!isLongTimeframe)
            return new[] { "5m", "15m", "30m", "1h", "2h" };

        return ltfProfile == "with-1h"
            ? new[] { "1h", "2h", "4h", "12h", "1d" }
            : new[] { "2h", "4h", "12h", "1d", "1w" };
    }

    private static IReadOnlySet<string> ResolveLtfAnchorRanges(bool isLongTimeframe, string ltfProfile)
    {
        if (!isLongTimeframe)
            return AnchorRangesWithWeek;

        return ltfProfile == "with-1h" ? AnchorRangesWithHour : AnchorRangesWithWeek;
    }

    private ActionResult? ValidateLtfProfile(string ltfProfile)
    {
        if (ltfProfile != "with-1w" && ltfProfile != "with-1h")
            return BadRequest("ltfProfile must be 'with-1w' or 'with-1h'.");

        return null;
    }

    private static ConfluenceDirectionMetricsDto EvaluateDirectionalConfluenceMetrics(
        IReadOnlyCollection<TurningPointStatsDto> stats,
        bool isBullish,
        int minTrendIndicators,
        IReadOnlySet<string> anchorRanges,
        int maxConflictingAnchorRanges)
    {
        return EvaluateDirectionalConfluenceMetricsWithThreshold(
            stats,
            isBullish,
            minAlignedRanges: 3,
            minAlignedAnchorRanges: 1,
            minTrendCountSum: minTrendIndicators,
            anchorRanges: anchorRanges,
            maxConflictingAnchorRanges: maxConflictingAnchorRanges);
    }

    private static IReadOnlyList<HeuristicProposalDto> DeriveHeuristics(IReadOnlyCollection<MissedTurningPointDto> missed)
    {
        var buySupport = 0;
        var sellSupport = 0;

        foreach (var point in missed)
        {
            var bullishRanges = point.Statistics.Count(s =>
                s.OscillatorBuyCount > s.OscillatorSellCount
                && s.MovingAverageBuyCount > s.MovingAverageSellCount
                && (s.OscillatorStrongTrendCount + s.OscillatorMediumTrendCount) > 0);

            var bearishRanges = point.Statistics.Count(s =>
                s.OscillatorSellCount > s.OscillatorBuyCount
                && s.MovingAverageSellCount > s.MovingAverageBuyCount
                && (s.OscillatorStrongTrendCount + s.OscillatorMediumTrendCount) > 0);

            if (point.ExpectedAdvice == "Buy" && bullishRanges >= 3)
            {
                buySupport++;
            }

            if (point.ExpectedAdvice == "Sell" && bearishRanges >= 3)
            {
                sellSupport++;
            }
        }

        return new List<HeuristicProposalDto>
        {
            new(
                "LongTimeframeBullishConfluence",
                "Buy",
                buySupport,
                "Trigger Buy when at least 3 time ranges show oscillator buy>sell, moving-average buy>sell, and at least medium trend."),
            new(
                "LongTimeframeBearishConfluence",
                "Sell",
                sellSupport,
                "Trigger Sell when at least 3 time ranges show oscillator sell>buy, moving-average sell>buy, and at least medium trend.")
        };
    }

    /// <summary>
    /// Returns the BIS (Bitcoin Indicator Statistics) snapshot at each local 14-day price maximum.
    /// AlignedRanges = number of LTF oscillator ranges that are bullish-aligned at that moment.
    /// TrendCountSum = total strong+medium trend indicator count across all LTF ranges.
    /// This helps discover indicator patterns around price peaks that are not yet directly used by the trading policy.
    /// </summary>
    [HttpGet("turning-points/local-highs/bis")]
    public async Task<ActionResult<IReadOnlyList<LocalHighBisDto>>> GetLocalHighBis(
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] int extremaWindowDays = TurningPointDetector.DefaultWindowDays,
        [FromQuery] string symbol = "BTC1D",
        [FromQuery] bool isLongTimeframe = true,
        [FromQuery] string ltfProfile = "with-1w",
        CancellationToken ct = default)
    {
        var extremaWindowValidationError = ValidateExtremaWindowDays(extremaWindowDays);
        if (extremaWindowValidationError is not null)
            return extremaWindowValidationError;

        var ltfProfileValidationError = ValidateLtfProfile(ltfProfile);
        if (ltfProfileValidationError is not null)
            return ltfProfileValidationError;

        var end = (endUtc ?? DateTime.UtcNow.Date.AddDays(-1).AddSeconds(-1).ToUniversalTime()).ToUniversalTime();
        var start = (startUtc ?? new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToUniversalTime();
        if (end <= start)
            return BadRequest("endUtc must be greater than startUtc.");

        var contextPadding = TimeSpan.FromDays(extremaWindowDays);
        var candles = await stockCandleRepository.GetStockCandlesUpToAsync(symbol, end + contextPadding, ct);
        var allCandles = candles
            .Where(x => x.Timestamp >= start - contextPadding && x.Timestamp <= end + contextPadding)
            .OrderBy(x => x.Timestamp)
            .ToList();

        if (allCandles.Count == 0)
            return NotFound($"No candles found for symbol '{symbol}' in selected range.");

        var localHighs = TurningPointDetector
            .DetectByWindow(allCandles, extremaWindowDays)
            .Where(x => x.Kind == TurningPointKind.LocalHigh && x.Timestamp >= start && x.Timestamp <= end)
            .OrderBy(x => x.Timestamp)
            .ToList();

        var timeRanges = ResolveLtfTimeRanges(isLongTimeframe, ltfProfile);
        var results = new List<LocalHighBisDto>(localHighs.Count);

        foreach (var high in localHighs)
        {
            var dayTime = new DateTime(high.Timestamp.Year, high.Timestamp.Month, high.Timestamp.Day, 23, 59, 59, DateTimeKind.Utc);
            var alignedRanges = 0;
            var trendCountSum = 0;
            var dataComplete = true;

            foreach (var timeRange in timeRanges)
            {
                try
                {
                    var osci = await bitcoinStatisticsService.GetStatistics(dayTime, true, timeRange, ct);
                    var osciStats = osci.TimeRangeStatistics.Single();
                    if (osciStats.IndicatorBuyCount > osciStats.IndicatorSellCount)
                        alignedRanges++;
                    trendCountSum += osciStats.IndicatorStrongTrendCount + osciStats.IndicatorMediumTrendCount;
                }
                catch (InvalidDataException)
                {
                    dataComplete = false;
                    break;
                }
            }

            results.Add(new LocalHighBisDto(
                high.Timestamp,
                high.ClosePrice,
                alignedRanges,
                trendCountSum,
                !dataComplete));
        }

        return Ok(results);
    }

    public record LocalHighBisDto(
        DateTime TimestampUtc,
        decimal ClosePrice,
        int AlignedRanges,
        int TrendCountSum,
        bool HasMissingData
    );
}
