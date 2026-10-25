using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Repository;

public interface IStockCandleRepository
{
    public Task<DateTime?> GetLatestTimestampForSymbolAsync(string symbol, CancellationToken cancellationToken);

    public Task<DateTime?> GetMaxTimestamp(string symbol, DateTime dateTimeNow, CancellationToken cancellationToken);

    public Task SaveAsync(IReadOnlyCollection<StockCandle> stockCandle, CancellationToken cancellationToken);

    public Task<StockCandle?> GetStockCandle (string symbol, DateTime dateTime, TimeSpan timeSpan, CancellationToken cancellationToken);

    public Task<IReadOnlyList<StockCandle>> GetStockCandlesUpToAsync(string symbol, DateTime endDate, CancellationToken cancellationToken);

    public Task<IReadOnlyList<StockCandle>> GetStockCandlesInRangeAsync(string symbol, DateTime startDate, DateTime endDate, CancellationToken cancellationToken);

    public Task<IReadOnlyCollection<DateTime>> GetDistinctTimestampsAsync(string symbol, DateTime startDate, DateTime endDate, CancellationToken cancellationToken);
}
