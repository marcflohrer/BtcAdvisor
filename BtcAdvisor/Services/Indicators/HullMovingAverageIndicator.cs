using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class HullMovingAverageIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "hma";

    public Signal Evaluate (IndicatorEvaluationInput indicatorEvaluationInput)
    {
        ArgumentNullException.ThrowIfNull (indicatorEvaluationInput);
        BaseIndicator.GetMaps(indicatorEvaluationInput,
                                           IndicatorId: IndicatorId,
                                           out var indicatorMapCurrent,
                                           indicatorMapPrevious: out var indicatorMapPrevious);
        if (indicatorMapCurrent == null)
        {
            Console.WriteLine($"ERROR: No current map found for indicator {IndicatorId}");
            return Signal.None;
        }

        var currentHMA = indicatorMapCurrent!.GetValue ("value");
        if (indicatorEvaluationInput.Previous == null)
        {
            return Signal.None;
        }

        if (double.IsNaN (currentHMA))
        {
            return Signal.None;
        }

        var previousHMA = indicatorMapPrevious!.GetValue ("value");
        if (double.IsNaN (previousHMA))
        {
            return Signal.None;
        }

        var currentPrice = indicatorEvaluationInput.Price;
        if (double.IsNaN (currentPrice))
        {
            return Signal.None;
        }

        // Kaufsignal, wenn der Preis den HMA von unten nach oben kreuzt
        if (currentPrice > currentHMA && currentPrice > previousHMA)
        {
            return Signal.Buy;
        }
        // Verkaufsignal, wenn der Preis den HMA von oben nach unten kreuzt
        else if (currentPrice < currentHMA && currentPrice < previousHMA)
        {
            return Signal.Sell;
        }
        else
        {
            return Signal.Neutral; // Neutral, wenn keine eindeutige Kreuzung festgestellt wird
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
