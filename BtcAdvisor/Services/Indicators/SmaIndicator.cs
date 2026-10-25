using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class SMAIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "sma";

    // Diese Methode setzt voraus, dass der aktuelle Preis sowie der SMA verfügbar sind
    public Signal Evaluate (IndicatorEvaluationInput indicatorEvaluationInput)
    {
        ArgumentNullException.ThrowIfNull (indicatorEvaluationInput);
        BaseIndicator.GetMaps(indicatorEvaluationInput,
                                           IndicatorId: IndicatorId,
                                           out var indicatorMapCurrent,
                                           indicatorMapPrevious: out var _,
                                           true);
        if (indicatorMapCurrent == null)
        {
            Console.WriteLine($"ERROR: No current map found for indicator {IndicatorId}");
            return Signal.None;
        }

        var currentPrice = indicatorEvaluationInput.Price;
        if (double.IsNaN (currentPrice))
        {
            return Signal.None;
        }

        var currentSMA = indicatorMapCurrent!.GetValue ("value");
        if (double.IsNaN (currentSMA))
        {
            return Signal.None;
        }

        // Kaufsignal, wenn der Preis von unter dem SMA auf über den SMA wechselt
        if (currentPrice > currentSMA)
        {
            return Signal.Buy;
        }
        // Verkaufsignal, wenn der Preis von über dem SMA auf unter den SMA wechselt
        else if (currentPrice < currentSMA)
        {
            return Signal.Sell;
        }
        else
        {
            return Signal.Neutral; // Neutral, wenn keine signifikante Änderung
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public bool HasPeriod() => true;

    public IReadOnlyCollection<int> GetPeriods() =>
    [
        10,
        20,
        50,
        100,
        200
    ];
}
