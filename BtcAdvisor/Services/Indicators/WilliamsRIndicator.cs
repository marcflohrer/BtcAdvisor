using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class WilliamsRIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "willr";

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

        var currentR = indicatorMapCurrent.GetValue ("value");
        if (double.IsNaN (currentR))
        {
            Console.WriteLine($"ERROR: current 'value' for indicator {IndicatorId} is NaN");
            return Signal.None;
        }

        if (indicatorMapPrevious == null)
        {
            Console.WriteLine($"ERROR: No previous map found for indicator {IndicatorId}");
            return Signal.None;
        }

        var previousR = indicatorMapPrevious.GetValue ("value");
        if (double.IsNaN (previousR))
        {
            Console.WriteLine($"ERROR: previous 'value' for indicator {IndicatorId} is NaN");
            return Signal.None;
        }

        // Kaufsignal, wenn der Indikator von unter -80 auf über -80 steigt
        if (currentR > -80 && previousR <= -80)
        {
            return Signal.Buy;
        }
        // Verkaufsignal, wenn der Indikator von über -20 auf unter -20 fällt
        else if (currentR < -20 && previousR >= -20)
        {
            return Signal.Sell;
        }
        else
        {
            return Signal.Neutral; // Neutral, wenn keine signifikante Schwelle überschritten wird
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
