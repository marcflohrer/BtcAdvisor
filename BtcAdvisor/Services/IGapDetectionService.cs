namespace BtcAdvisor.Services;

public interface IGapDetectionService
{
    Task<TimeframeGapCheckResult> DetectCandleGapsAsync(string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken);

    Task<TimeframeGapCheckResult> DetectIndicatorGapsAsync(string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken);
}