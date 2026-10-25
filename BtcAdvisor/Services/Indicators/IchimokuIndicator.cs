using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class IchimokuIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "ichimoku";

    public Signal Evaluate (IndicatorEvaluationInput indicatorEvaluationInput)
    {
        ArgumentNullException.ThrowIfNull (indicatorEvaluationInput);
        BaseIndicator.GetMaps(indicatorEvaluationInput,
                                           IndicatorId: IndicatorId,
                                           out var indicatorMapCurrent,
                                           indicatorMapPrevious: out var _);
        if (indicatorMapCurrent == null)
        {
            Console.WriteLine($"ERROR: No current map found for indicator {IndicatorId}");
            return Signal.None;
        }
        
        var conversionLine = indicatorMapCurrent!.GetValue ("conversion");
        if (double.IsNaN (conversionLine))
        {
            return Signal.None;
        }

        var baseLine = indicatorMapCurrent.GetValue ("base");
        if (double.IsNaN (baseLine))
        {
            return Signal.None;
        }

        var spanA = indicatorMapCurrent.GetValue ("spanA");
        if (double.IsNaN (spanA))
        {
            return Signal.None;
        }

        var spanB = indicatorMapCurrent.GetValue ("spanB");
        if (double.IsNaN (spanB))
        {
            return Signal.None;
        }

        var currentSpanA = indicatorMapCurrent.GetValue ("currentSpanA");
        if (double.IsNaN (currentSpanA))
        {
            return Signal.None;
        }

        var currentSpanB = indicatorMapCurrent.GetValue ("currentSpanB");
        if (double.IsNaN (currentSpanB))
        {
            return Signal.None;
        }

        // var laggingSpanA = indicatorMap.GetValue("laggingSpanA");
        // var laggingSpanB = indicatorMap.GetValue("laggingSpanB");
        var currentPrice = indicatorEvaluationInput.Price;
        if (double.IsNaN (currentPrice))
        {
            return Signal.None;
        }

        // Evaluieren der Position des Preises relativ zur aktuellen und zukünftigen Cloud sowie der Tenkan-Kijun Kreuzung
        var isPriceAboveCloud = !(currentPrice <= Math.Max (currentSpanA, currentSpanB) || currentPrice <= Math.Max (spanA, spanB));
        var isPriceBelowCloud = !(currentPrice >= Math.Min (currentSpanA, currentSpanB) || currentPrice >= Math.Min (spanA, spanB));
        var isConversionAboveBase = conversionLine > baseLine;

        if (isPriceAboveCloud && isConversionAboveBase)
        {
            return Signal.Buy; // Kaufsignal: Preis und Umkehrlinie sind über der Cloud
        }
        else if (isPriceBelowCloud && conversionLine < baseLine)
        {
            return Signal.Sell; // Verkaufsignal: Preis und Umkehrlinie sind unter der Cloud
        }
        else
        {
            return Signal.Neutral; // Neutral: Keine klare Richtung aus der Analyse ableitbar
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
