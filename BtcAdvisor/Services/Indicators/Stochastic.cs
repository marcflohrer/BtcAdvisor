using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class Stochastic : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "stoch";

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

        var stochasticK = indicatorMapCurrent!.GetValue ("valueK");
        if (double.IsNaN (stochasticK))
        {
            return Signal.None;
        }

        var stochasticD = indicatorMapCurrent.GetValue ("valueD");
        if (double.IsNaN (stochasticD))
        {
            return Signal.None;
        }

        // Verkaufssignal, wenn beide Linien über 80 sind
        if (stochasticK > 80 && stochasticD > 80)
        {
            return Signal.Sell;
        }
        // Kaufsignal, wenn beide Linien unter 20 sind
        else if (stochasticK < 20 && stochasticD < 20)
        {
            return Signal.Buy;
        }
        else
        {
            // Neutral, wenn keine der Bedingungen erfüllt ist
            return Signal.Neutral;
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
