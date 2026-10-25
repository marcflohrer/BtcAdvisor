using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class MomentumIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "mom";

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

        var currentMomentum = indicatorMapCurrent!.GetValue ("value");
        if (indicatorEvaluationInput.Previous == null)
        {
            return Signal.None;
        }
        if (double.IsNaN (currentMomentum))
        {
            return Signal.None;
        }

        var previousMomentum = indicatorMapPrevious!.GetValue ("value");
        if (double.IsNaN (previousMomentum))
        {
            return Signal.None;
        }

        // Kaufsignal, wenn das aktuelle Momentum positiv wird (Übergang von negativ zu positiv)
        if (currentMomentum > 0 && previousMomentum <= 0)
        {
            return Signal.Buy;
        }
        // Verkaufsignal, wenn das aktuelle Momentum negativ wird (Übergang von positiv zu negativ)
        else if (currentMomentum < 0 && previousMomentum >= 0)
        {
            return Signal.Sell;
        }
        else
        {
            return Signal.Neutral; // Neutral, wenn keine signifikante Änderung in der Momentum-Richtung
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
