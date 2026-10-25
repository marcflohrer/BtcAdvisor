using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;

public sealed class CandleBackfillService(
    IHistoricalBinanceCandleService historicalBinanceCandleService,
    IStockCandleRepository stockCandleRepository,
    IBinanceKlineService binanceKlineService) : ICandleBackfillService
{
    private readonly IHistoricalBinanceCandleService _historicalBinanceCandleService = historicalBinanceCandleService;
    private readonly IStockCandleRepository _stockCandleRepository = stockCandleRepository;
    private readonly IBinanceKlineService _binanceKlineService = binanceKlineService;

    public async Task<BackfillOperationResult> BackfillMissingCandlesAsync(string interval, IReadOnlyCollection<DateTime> missingTimestampsUtc, CancellationToken cancellationToken)
    {
        var requested = missingTimestampsUtc
            .Select(ts => DateTime.SpecifyKind(ts, DateTimeKind.Utc))
            .Distinct()
            .OrderBy(ts => ts)
            .ToList();

        if (requested.Count == 0)
        {
            return new BackfillOperationResult(interval, 0, 0, [], []);
        }

        var symbol = BackfillTimeframeCatalog.GetDatabaseSymbol(interval);
        var duration = BackfillTimeframeCatalog.GetIntervalDuration(interval);
        var rangeStart = requested[0] - duration;
        var rangeEnd = requested[^1];
        var history = await _historicalBinanceCandleService.GetCandlesRangeAsync(interval, rangeStart, rangeEnd, cancellationToken);
        var historyByClose = history.ToDictionary(c => DateTime.SpecifyKind(c.CloseTime, DateTimeKind.Utc), c => c);
        var existing = await _stockCandleRepository.GetDistinctTimestampsAsync(symbol, requested[0], requested[^1], cancellationToken);
        var existingSet = existing
            .Select(ts => DateTime.SpecifyKind(ts, DateTimeKind.Utc))
            .ToHashSet();

        var toPersist = new List<StockCandle>();
        var filled = new List<DateTime>();
        var skipped = new List<DateTime>();

        foreach (var timestamp in requested)
        {
            if (existingSet.Contains(timestamp))
            {
                skipped.Add(timestamp);
                continue;
            }

            if (!historyByClose.TryGetValue(timestamp, out var candle))
            {
                skipped.Add(timestamp);
                continue;
            }

            toPersist.Add(new StockCandle
            {
                Symbol = symbol,
                OpenPrice = (decimal)candle.Open,
                HighPrice = (decimal)candle.High,
                LowPrice = (decimal)candle.Low,
                ClosePrice = (decimal)candle.Close,
                Volume = checked((long)Math.Round(candle.Volume, MidpointRounding.AwayFromZero)),
                Timestamp = timestamp
            });
            filled.Add(timestamp);
        }

        if (toPersist.Count > 0)
        {
            await _stockCandleRepository.SaveAsync(toPersist, cancellationToken);
            _binanceKlineService.InvalidateInterval(interval);
        }

        return new BackfillOperationResult(interval, requested.Count, filled.Count, filled, skipped);
    }
}