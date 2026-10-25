using BtcAdvisor.Repository;

namespace BtcAdvisor.Services;

public sealed class GapDetectionService(
    IHistoricalBinanceCandleService historicalBinanceCandleService,
    IStockCandleRepository stockCandleRepository,
    IBitcoinRepository bitcoinRepository) : IGapDetectionService
{
    private readonly IHistoricalBinanceCandleService _historicalBinanceCandleService = historicalBinanceCandleService;
    private readonly IStockCandleRepository _stockCandleRepository = stockCandleRepository;
    private readonly IBitcoinRepository _bitcoinRepository = bitcoinRepository;

    public async Task<TimeframeGapCheckResult> DetectCandleGapsAsync(string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {
        var symbol = BackfillTimeframeCatalog.GetDatabaseSymbol(interval);
        var expectedCandles = await _historicalBinanceCandleService.GetCandlesRangeAsync(interval, startUtc, endUtc, cancellationToken);
        var actualTimestamps = await _stockCandleRepository.GetDistinctTimestampsAsync(symbol, startUtc, endUtc, cancellationToken);

        return BuildGapCheckResult(interval, symbol, expectedCandles.Select(c => c.CloseTime), actualTimestamps);
    }

    public async Task<TimeframeGapCheckResult> DetectIndicatorGapsAsync(string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {
        var symbol = BackfillTimeframeCatalog.GetDatabaseSymbol(interval);
        var expectedCandles = await _historicalBinanceCandleService.GetCandlesRangeAsync(interval, startUtc, endUtc, cancellationToken);
        var actualTimestamps = await _bitcoinRepository.GetDistinctTimestampsAsync(
            startUtc,
            endUtc,
            BackfillTimeframeCatalog.GetPeriodMinutes(interval),
            cancellationToken);

        return BuildGapCheckResult(interval, symbol, expectedCandles.Select(c => c.CloseTime), actualTimestamps);
    }

    internal static TimeframeGapCheckResult BuildGapCheckResult(string interval, string symbol, IEnumerable<DateTime> expectedTimestamps, IEnumerable<DateTime> actualTimestamps)
    {
        var duration = BackfillTimeframeCatalog.GetIntervalDuration(interval);
        var normalizedExpected = expectedTimestamps
            .Select(NormalizeTimestamp)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        var normalizedActual = actualTimestamps
            .Select(NormalizeTimestamp)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        var actualSet = normalizedActual.ToHashSet();

        var missing = normalizedExpected
            .Where(ts => !actualSet.Contains(ts))
            .ToList();

        return new TimeframeGapCheckResult(
            interval,
            symbol,
            BackfillTimeframeCatalog.GetPeriodMinutes(interval),
            normalizedExpected.Count,
            normalizedActual.Count,
            missing,
            CompressMissingRanges(missing, duration));
    }

    private static IReadOnlyList<MissingTimestampRange> CompressMissingRanges(IReadOnlyList<DateTime> missingTimestamps, TimeSpan intervalDuration)
    {
        if (missingTimestamps.Count == 0)
        {
            return [];
        }

        var ranges = new List<MissingTimestampRange>();
        var rangeStart = missingTimestamps[0];
        var previous = missingTimestamps[0];
        var count = 1;

        for (var index = 1; index < missingTimestamps.Count; index++)
        {
            var current = missingTimestamps[index];
            if (current - previous == intervalDuration)
            {
                previous = current;
                count++;
                continue;
            }

            ranges.Add(new MissingTimestampRange(rangeStart, previous, count));
            rangeStart = current;
            previous = current;
            count = 1;
        }

        ranges.Add(new MissingTimestampRange(rangeStart, previous, count));
        return ranges;
    }

    private static DateTime NormalizeTimestamp(DateTime timestamp)
        => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
}