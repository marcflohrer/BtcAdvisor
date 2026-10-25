using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class RelativeStabilityIndex : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "rsi";

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

        var rsi = indicatorMapCurrent!.GetValue ("value");
        if (double.IsNaN (rsi))
        {
            return Signal.None;
        }

        if (rsi > 70)
        {
            return Signal.Sell;
        }
        else if (rsi < 30)
        {
            return Signal.Buy;
        }
        else
        {
            return Signal.Neutral;
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
