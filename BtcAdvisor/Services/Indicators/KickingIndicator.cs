using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public class KickingIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "kickingbylength";

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

        var currentKicking = indicatorMapCurrent!.GetValue ("value");
        if (double.IsNaN (currentKicking))
        {
            return Signal.None;
        }

        if (indicatorEvaluationInput.Previous == null)
        {
            return Signal.None;
        }

        if (currentKicking == 100)
        {
            return Signal.Buy; // Bullisches Kicking-Muster
        }
        else if (currentKicking == -100)
        {
            return Signal.Sell; // Bärisches Kicking-Muster
        }
        else
        {
            return Signal.Neutral; // Kein Kicking-Muster gefunden
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
