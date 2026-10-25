using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;

public class BitcoinIndicatorService (IBitcoinRepository bitcoinRepository) : IBitcoinIndicatorService
{
    private readonly IBitcoinRepository _bitcoinRepository = bitcoinRepository;

    public async Task<IndicatorEvaluationInput?> GetFormattedIndicatorData (DateTime dateTime, string timeSpan, string indicator, int? period, CancellationToken cancellationToken)
    {
        var indicatorData = await _bitcoinRepository.GetIndicatorStatistics (dateTime, timeSpan, indicator, period, cancellationToken);
        if (indicatorData == null || indicatorData.Current == null)
        {
            return null;
        }

        var transformedCurrent = TransformIndicatorData(indicatorData.Current, timeSpan, true);

        // Transformieren der Daten in das benötigte Modell
        Dictionary<string, IndicatorInputDatum> transformedPrevious;
        if (indicatorData.Previous != null)
        {
            transformedPrevious = TransformIndicatorData(indicatorData.Previous, timeSpan, false);
        }
        else
        {
            Console.WriteLine("ERROR Previous is null!");
            throw new InvalidDataException("Previous propery is null!");
        }

        return new IndicatorEvaluationInput
        {
            Current = transformedCurrent,
            Previous = transformedPrevious,
            DateTime = indicatorData.DateTime,
        };
    }

    private static Dictionary<string, IndicatorInputDatum> TransformIndicatorData (Dictionary<string, List<IndicatorResult>> mapIndicatorToValues, string timeSpan, bool isCurrent)
    {
        var transformedDictionary = new Dictionary<string, IndicatorInputDatum> ();
        foreach (string key in mapIndicatorToValues.Keys)
        {
            var indicatorInputDatum = mapIndicatorToValues[key];
            foreach (var result in indicatorInputDatum)
            {
                if(!result.IndicatorId.Contains($"_{timeSpan}_"))
                {
                    Console.WriteLine($"Requesting period {timeSpan}: Dropping indicator {result.IndicatorId} with wrong timespan and indicator {result.Indicator} (isCurrent = {isCurrent})");
                    continue;
                }

                var transformed = new IndicatorInputDatum
                {
                    Indicator = result.Indicator
                };
                transformed.AddOrUpdateValue ("value", result.Value ?? double.NaN);
                transformed.AddOrUpdateValue ("valueD", result.ValueD ?? double.NaN);
                transformed.AddOrUpdateValue ("valueFastK", result.ValueFastK ?? double.NaN);
                transformed.AddOrUpdateValue ("valueFastD", result.ValueFastD ?? double.NaN);
                transformed.AddOrUpdateValue ("valueK", result.ValueK ?? double.NaN);
                transformed.AddOrUpdateValue ("base", result.Base ?? double.NaN);
                transformed.AddOrUpdateValue ("conversion", result.Conversion ?? double.NaN);
                transformed.AddOrUpdateValue ("currentSpanA", result.CurrentSpanA ?? double.NaN);
                transformed.AddOrUpdateValue ("currentSpanB", result.CurrentSpanB ?? double.NaN);
                transformed.AddOrUpdateValue ("laggingSpanA", result.LaggingSpanA ?? double.NaN);
                transformed.AddOrUpdateValue ("laggingSpanB", result.LaggingSpanB ?? double.NaN);
                transformed.AddOrUpdateValue ("spanA", result.SpanA ?? double.NaN);
                transformed.AddOrUpdateValue ("spanB", result.SpanB ?? double.NaN);
                transformed.AddOrUpdateValue ("valueMacd", result.ValueMacd ?? double.NaN);
                transformed.AddOrUpdateValue ("valueMacdhist", result.ValueMacdhist ?? double.NaN);
                transformed.AddOrUpdateValue ("valueMacdsignal", result.ValueMacdsignal ?? double.NaN);
                transformedDictionary.Add (result.IndicatorId, transformed);
            }
        }

        return transformedDictionary;
    }
}
