using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public partial class ADXIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "adx";

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
        var adx = indicatorMapCurrent!.GetValue ("value");
        if (double.IsNaN (adx))
        {
            return Signal.None;
        }

        if (adx >= 49)
        {
            return Signal.StrongTrend; // Strong Trend
        }
        else 
        if (adx >= 39)
        {
            return Signal.MediumTrend; // Medium strong Trend
        }
        else if (adx >= 29)
        {
            return Signal.WeakTrend; // Weak Trend
        }
        else 
        {
            return Signal.NoTrend; // Keinen Trend anzeigen
        }
    }

    public bool CanHandle (string someIndicator) => someIndicator == IndicatorId;

    public IReadOnlyCollection<int> GetPeriods() => [];
}
