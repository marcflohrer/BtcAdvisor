using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class StochasticFastIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "stochf";

    public Signal Evaluate (IndicatorEvaluationInput indicatorEvaluationInput)
    {
        ArgumentNullException.ThrowIfNull(indicatorEvaluationInput);
        BaseIndicator.GetMaps(indicatorEvaluationInput,
                                           IndicatorId: IndicatorId,
                                           out var indicatorMapCurrent,
                                           indicatorMapPrevious: out var indicatorMapPrevious);
        if (indicatorMapCurrent == null)
        {
            Console.WriteLine($"ERROR: No current map found for indicator {IndicatorId}");
            return Signal.None;
        }

        var currentK = indicatorMapCurrent!.GetValue("valueFastK");
        if (double.IsNaN(currentK))
        {
            Console.WriteLine($"ERROR: 'current' valueFastK is NaN {IndicatorId}");
            return Signal.None;
        }

        var currentD = indicatorMapCurrent.GetValue("valueFastD");
        if (double.IsNaN(currentD))
        {
            Console.WriteLine($"ERROR: 'current' valueFastD is NaN {IndicatorId}");
            return Signal.None;
        }

        if (indicatorMapPrevious == null)
        {
            return Signal.None;
        }

        var previousK = indicatorMapPrevious.GetValue("valueFastK");
        if (double.IsNaN(previousK))
        {
            Console.WriteLine($"ERROR: 'previous' valueFastK is NaN {IndicatorId}");
            return Signal.None;
        }

        var previousD = indicatorMapPrevious.GetValue("valueFastD");
        if (double.IsNaN(previousD))
        {
            Console.WriteLine($"ERROR: 'previous' valueFastD is NaN {IndicatorId}");
            return Signal.None;
        }

        // Kaufsignal, wenn %K die %D-Linie von unten kreuzt
        if (currentK > currentD && previousK <= previousD)
        {
            return Signal.Buy;
        }
        // Verkaufsignal, wenn %K die %D-Linie von oben kreuzt
        else if (currentK < currentD && previousK >= previousD)
        {
            return Signal.Sell;
        }
        else
        {
            return Signal.Neutral; // Neutral, wenn keine Kreuzung erfolgt
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
