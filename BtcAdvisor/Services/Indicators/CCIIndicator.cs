using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Indicators;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public class CCIIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "cci";

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

        var cci = indicatorMapCurrent!.GetValue ("value");
        if (indicatorEvaluationInput.Previous == null)
        {
            return Signal.None;
        }
        if (double.IsNaN (cci))
        {
            return Signal.None;
        }

        // Verkaufssignal, wenn CCI über 100
        if (cci > 100)
        {
            return Signal.Sell;
        }
        // Kaufsignal, wenn CCI unter -100
        else if (cci < -100)
        {
            return Signal.Buy;
        }
        else
        {
            // Neutral, wenn CCI zwischen -100 und 100
            return Signal.Neutral;
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
