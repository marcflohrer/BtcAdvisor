using BtcAdvisor.Constants;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;
using static BtcAdvisor.Services.Indicators.BaseIndicator;

namespace BtcAdvisor.Services.Indicators;

public class EMAIndicator : ICanHandle, IIsSignalIndicator
{
    public const string IndicatorId = "ema";

    public Signal Evaluate(IndicatorEvaluationInput indicatorEvaluationInput)
    {
        ArgumentNullException.ThrowIfNull(indicatorEvaluationInput);
        if (indicatorEvaluationInput.Current == null)
        {
            return Signal.None;
        }

        var currentIndicatorKey = GetCurrentIndicatorName(indicatorEvaluationInput, IndicatorId, true);
        if(!indicatorEvaluationInput.Current.TryGetValue(currentIndicatorKey, out var indicatorMapCurrentLongTerm))
        {
            bool isEma10Missing = currentIndicatorKey.IndexOf("ema_10_") == -1;
            if(isEma10Missing)
            {
                Console.WriteLine($"ERROR: Key '{currentIndicatorKey}' not found in indicator dictionary. EMA 10 Missing: {isEma10Missing}");
                Console.WriteLine($"Known keys: {string.Join(',', indicatorEvaluationInput.Current.Keys.ToList())}");   
            }
            return Signal.None;
        }

        if(!BitcoinConstants.EmaMapToShortTermIndicator.TryGetValue(currentIndicatorKey, out var shortTermIndicator))
        {
            return Signal.None;
        }

        if(!indicatorEvaluationInput.Current.TryGetValue(shortTermIndicator, out var indicatorMapCurrentShortTerm))
        {
            Console.WriteLine($"ERROR: {indicatorEvaluationInput} Unknown key in short term indicator dictionary: {shortTermIndicator} - Known values: {string.Join(',', indicatorEvaluationInput.Current.Keys.ToList())}");
            return Signal.None;
        }
        
        var longTerm = indicatorMapCurrentLongTerm.GetValue("value");
        if(double.IsNaN(longTerm))
        {
            return Signal.None;
        }

        var shortTerm = indicatorMapCurrentShortTerm.GetValue("value");
        if(double.IsNaN(shortTerm))
        {
            return Signal.None;
        }

        // Kaufsignal: kurzfristiger EMA kreuzt langfristigen EMA von unten
        if (shortTerm > longTerm)
        {
            return Signal.Buy;
        }
        // Verkaufsignal: kurzfristiger EMA kreuzt langfristigen EMA von oben
        else if (shortTerm < longTerm)
        {
            return Signal.Sell;
        }
        else
        {
            return Signal.Neutral; // Neutral, wenn keine Kreuzung erfolgt
        }
    }

    public bool CanHandle(string someIndicator) => someIndicator == IndicatorId;

    public bool HasPeriod() => true;

    public IReadOnlyCollection<int> GetPeriods() =>
        [
            10,
            20,
            50,
            100,
            200
        ];
}
