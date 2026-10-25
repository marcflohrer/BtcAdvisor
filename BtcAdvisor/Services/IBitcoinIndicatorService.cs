using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;

public interface IBitcoinIndicatorService
{
    Task<IndicatorEvaluationInput?> GetFormattedIndicatorData (DateTime dateTime, string timeSpan, string indicator, int? period, CancellationToken cancellationToken);
}
