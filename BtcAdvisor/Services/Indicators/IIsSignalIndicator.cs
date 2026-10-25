using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Indicators;

public interface IIsSignalIndicator
{
    public Signal Evaluate (IndicatorEvaluationInput indicatorEvaluationInput);

    public bool HasPeriod() => false;

    IReadOnlyCollection<int> GetPeriods();
}
