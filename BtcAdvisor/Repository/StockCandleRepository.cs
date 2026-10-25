using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository.Models;
using static BtcAdvisor.Extensions.DateTimeExtensions;

namespace BtcAdvisor.Repository;

public class StockCandleRepository (ApplicationDbContext applicationDbContext) : IStockCandleRepository
{
    private readonly ApplicationDbContext _applicationDbContext = applicationDbContext;

    public async Task<DateTime?> GetLatestTimestampForSymbolAsync(string symbol, CancellationToken cancellationToken)
    {
        return await _applicationDbContext.StockCandles
            .Where(c => c.Symbol == symbol)
            .Select(c => (DateTime?)c.Timestamp)
            .MaxAsync(cancellationToken);
    }

    public async Task SaveAsync(IReadOnlyCollection<StockCandle> stockCandles, CancellationToken cancellationToken)
    {
        await _applicationDbContext.StockCandles.AddRangeAsync(stockCandles, cancellationToken);
        await _applicationDbContext.SaveChangesAsync(cancellationToken);
        var count = await _applicationDbContext.StockCandles.CountAsync(cancellationToken: cancellationToken);
        Console.WriteLine($"{stockCandles.FirstOrDefault()?.Timestamp} Currently {count} stock candles in database: adding {stockCandles.FirstOrDefault()?.Symbol} ::  {stockCandles.FirstOrDefault()?.Timestamp} :: {stockCandles.FirstOrDefault()?.ClosePrice}.");
    }

    public async Task<DateTime?> GetMaxTimestamp (string symbol, DateTime dateTime, CancellationToken cancellationToken)
    {
        if (_applicationDbContext.StockCandles is null)
        {
            throw new InvalidOperationException ("Database column stockcandle is null");
        }
        if(!await _applicationDbContext.StockCandles.AnyAsync(cancellationToken: cancellationToken))
        {
            throw new InvalidDataException("No data in database table stockcandle found!");
        }

        var timeStampCount = await _applicationDbContext
            .StockCandles
            .Where(id => id.Symbol == symbol && id.Timestamp <= dateTime.ToUtcDateTime())
            .CountAsync(cancellationToken: cancellationToken);
        if (timeStampCount == 0)
        {
            return null;
        }

        var maxTimestamp = await _applicationDbContext
            .StockCandles
            .Where(id => id.Symbol == symbol && id.Timestamp <= dateTime.ToUtcDateTime())
            .MaxAsync(id => id.Timestamp, cancellationToken: cancellationToken);

        return maxTimestamp;
    }

    public async Task<StockCandle?> GetStockCandle(string symbol, DateTime dateTime, TimeSpan timeSpan, CancellationToken cancellationToken)
    {
        if (_applicationDbContext.StockCandles is null)
        {
            throw new InvalidOperationException("Database column stockcandle is null");
        }
        if (!await _applicationDbContext.StockCandles.AnyAsync(cancellationToken: cancellationToken))
        {
            throw new InvalidDataException("No data in database table stockcandle found!");
        }

        var startDateTime = dateTime.ToUtcDateTime();
        var endDateTime = dateTime.AddMinutes(timeSpan.TotalMinutes).ToUtcDateTime();

        // Abfrage aller IndicatorData inklusive der IndicatorResults, sortiert nach DateCreated absteigend
        var stockCandle = await _applicationDbContext.StockCandles
            .Where(id => id.Symbol == symbol && id.Timestamp >= startDateTime && id.Timestamp <= endDateTime)
            .OrderByDescending(x => x.Timestamp)
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);

        return stockCandle;
    }

    public async Task<IReadOnlyList<StockCandle>> GetStockCandlesUpToAsync(string symbol, DateTime endDate, CancellationToken cancellationToken)
    {
        var end = endDate.ToUtcDateTime();
        var candles = await _applicationDbContext.StockCandles
            .Where(c => c.Symbol == symbol && c.Timestamp <= end)
            .OrderBy(c => c.Timestamp)
            .ToListAsync(cancellationToken);
        return candles.AsReadOnly();
    }

    public async Task<IReadOnlyList<StockCandle>> GetStockCandlesInRangeAsync(string symbol, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        var startUtc = startDate.ToUtcDateTime();
        var endUtc = endDate.ToUtcDateTime();

        var candles = await _applicationDbContext.StockCandles
            .Where(c => c.Symbol == symbol && c.Timestamp >= startUtc && c.Timestamp <= endUtc)
            .OrderBy(c => c.Timestamp)
            .ToListAsync(cancellationToken);

        return candles.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<DateTime>> GetDistinctTimestampsAsync(string symbol, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        var startUtc = startDate.ToUtcDateTime();
        var endUtc = endDate.ToUtcDateTime();

        var timestamps = await _applicationDbContext.StockCandles
            .Where(c => c.Symbol == symbol && c.Timestamp >= startUtc && c.Timestamp <= endUtc)
            .Select(c => c.Timestamp)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        return timestamps.AsReadOnly();
    }
}
