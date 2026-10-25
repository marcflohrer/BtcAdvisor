using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class AwesomeOscillator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "ao";

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

        var currentAO = indicatorMapCurrent!.GetValue ("value");
        if (double.IsNaN (currentAO))
        {
            return Signal.None;
        }

        if (indicatorEvaluationInput.Previous == null)
        {
            return Signal.None;
        }

        var previousAO = indicatorMapPrevious!.GetValue ("value");
        if (double.IsNaN (previousAO))
        {
            return Signal.None;
        }

        if (currentAO > 0 && previousAO < 0)
        {
            return Signal.Buy; // Kaufsignal bei Übergang von negativ zu positiv
        }
        else if (currentAO < 0 && previousAO > 0)
        {
            return Signal.Sell; // Verkaufsignal bei Übergang von positiv zu negativ
        }
        else
        {
            return Signal.Neutral; // Neutral, wenn keine klaren Signale vorhanden sind
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
