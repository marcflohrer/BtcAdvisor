namespace BtcAdvisor.Services;

public interface IHistoricalBinanceCandleService
{
    Task<IReadOnlyList<OhlcvCandle>> GetCandlesRangeAsync(string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken);
}