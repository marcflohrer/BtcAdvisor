using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class MacdIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "macd";

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

        var currentMacd = indicatorMapCurrent!.GetValue ("valueMacd");
        if (double.IsNaN (currentMacd))
        {
            return Signal.None;
        }

        var currentSignalLine = indicatorMapCurrent.GetValue ("valueMacdsignal");
        if (double.IsNaN (currentSignalLine))
        {
            return Signal.None;
        }

        if (indicatorMapPrevious == null)
        {
            return Signal.None;
        }

        var previousMacd = indicatorMapPrevious.GetValue ("valueMacd");
        if (double.IsNaN (previousMacd))
        {
            return Signal.None;
        }

        var previousSignalLine = indicatorMapPrevious!.GetValue ("valueMacdsignal");
        if (double.IsNaN (previousSignalLine))
        {
            return Signal.None;
        }

        // Kaufsignal, wenn die MACD-Linie die Signallinie von unten kreuzt
        if (currentMacd > currentSignalLine && previousMacd <= previousSignalLine)
        {
            return Signal.Buy;
        }
        // Verkaufsignal, wenn die MACD-Linie die Signallinie von oben kreuzt
        else if (currentMacd < currentSignalLine && previousMacd >= previousSignalLine)
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
