using BtcAdvisor.Constants;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;

public sealed class IndicatorBackfillService(
    IBitcoinIndicatorService bitcoinIndicatorService,
    IBitcoinRepository bitcoinRepository,
    IBinanceKlineService binanceKlineService) : IIndicatorBackfillService
{
    private readonly IBitcoinIndicatorService _bitcoinIndicatorService = bitcoinIndicatorService;
    private readonly IBitcoinRepository _bitcoinRepository = bitcoinRepository;
    private readonly IBinanceKlineService _binanceKlineService = binanceKlineService;

    public async Task<BackfillOperationResult> BackfillMissingIndicatorsAsync(string interval, IReadOnlyCollection<DateTime> missingTimestampsUtc, CancellationToken cancellationToken)
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

        _binanceKlineService.InvalidateInterval(interval);

        var filled = new List<DateTime>();
        var skipped = new List<DateTime>();
        foreach (var timestamp in requested)
        {
            var indicatorDatum = await BuildIndicatorDatumAsync(interval, timestamp, cancellationToken);
            if (indicatorDatum is null || indicatorDatum.IndicatorResults.Count == 0)
            {
                skipped.Add(timestamp);
                continue;
            }

            await _bitcoinRepository.SaveIndicatorData(indicatorDatum, cancellationToken);
            filled.Add(timestamp);
        }

        return new BackfillOperationResult(interval, requested.Count, filled.Count, filled, skipped);
    }

    private async Task<IndicatorDatum?> BuildIndicatorDatumAsync(string interval, DateTime timestamp, CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, IndicatorResult>(StringComparer.Ordinal);
        var emaCollected = false;

        foreach (var indicatorSpec in BitcoinConstants.IndicatorList())
        {
            if (indicatorSpec.Name == "ema")
            {
                if (emaCollected)
                {
                    continue;
                }

                emaCollected = true;
            }

            var input = await _bitcoinIndicatorService.GetFormattedIndicatorData(
                timestamp,
                interval,
                indicatorSpec.Name,
                indicatorSpec.Period,
                cancellationToken);

            if (input is null)
            {
                continue;
            }

            foreach (var (indicatorId, datum) in input.Current)
            {
                if (results.ContainsKey(indicatorId))
                {
                    continue;
                }

                results[indicatorId] = ToIndicatorResult(indicatorId, datum);
            }
        }

        if (results.Count == 0)
        {
            return null;
        }

        return new IndicatorDatum
        {
            ApiId = $"local_{interval}_{timestamp:yyyyMMddHHmmss}",
            Indicator = "local-bulk",
            PeriodMinutes = BackfillTimeframeCatalog.GetPeriodMinutes(interval),
            DateCreated = timestamp,
            IndicatorResults = results.Values.ToList()
        };
    }

    private static IndicatorResult ToIndicatorResult(string indicatorId, IndicatorInputDatum datum)
    {
        datum.Values.TryGetValue("value", out var value);
        datum.Values.TryGetValue("valueD", out var valueD);
        datum.Values.TryGetValue("valueFastK", out var valueFastK);
        datum.Values.TryGetValue("valueFastD", out var valueFastD);
        datum.Values.TryGetValue("valueK", out var valueK);
        datum.Values.TryGetValue("base", out var baseValue);
        datum.Values.TryGetValue("conversion", out var conversion);
        datum.Values.TryGetValue("currentSpanA", out var currentSpanA);
        datum.Values.TryGetValue("currentSpanB", out var currentSpanB);
        datum.Values.TryGetValue("laggingSpanA", out var laggingSpanA);
        datum.Values.TryGetValue("laggingSpanB", out var laggingSpanB);
        datum.Values.TryGetValue("spanA", out var spanA);
        datum.Values.TryGetValue("spanB", out var spanB);
        datum.Values.TryGetValue("valueMacd", out var valueMacd);
        datum.Values.TryGetValue("valueMacdhist", out var valueMacdhist);
        datum.Values.TryGetValue("valueMacdsignal", out var valueMacdsignal);

        return new IndicatorResult
        {
            IndicatorId = indicatorId,
            Indicator = datum.Indicator ?? string.Empty,
            Value = value,
            ValueD = valueD,
            ValueFastK = valueFastK,
            ValueFastD = valueFastD,
            ValueK = valueK,
            Base = baseValue,
            Conversion = conversion,
            CurrentSpanA = currentSpanA,
            CurrentSpanB = currentSpanB,
            LaggingSpanA = laggingSpanA,
            LaggingSpanB = laggingSpanB,
            SpanA = spanA,
            SpanB = spanB,
            ValueMacd = valueMacd,
            ValueMacdhist = valueMacdhist,
            ValueMacdsignal = valueMacdsignal
        };
    }
}