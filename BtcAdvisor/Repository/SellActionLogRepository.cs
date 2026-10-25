using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository.Models;
using static BtcAdvisor.Extensions.DateTimeExtensions;

namespace BtcAdvisor.Repository;

public class SellActionLogRepository (IServiceScopeFactory serviceScopeFactory) : ISellActionLogRepository
{
    public async Task<IReadOnlyCollection<SellActionLog>> GetFireSellActionLogs(DateTime startHistory, DateTime endHistory, bool? isLongTimeframe, CancellationToken cancellationToken)
    {
        var applicationDbContext = serviceScopeFactory.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (applicationDbContext.SellActionLogs is null)
        {
            throw new InvalidOperationException("Database column is null");
        }
        if (!await applicationDbContext.SellActionLogs.AnyAsync(cancellationToken: cancellationToken))
        {
            return new ReadOnlyCollection<SellActionLog>(new List<SellActionLog>());
        }
        var startHistoryUtc = startHistory.ToUtcDateTime();
        var endHistoryUtc = endHistory.ToUtcDateTime();

        // Abfrage aller SellActionLogs sortiert nach TradingDate absteigend
        var query = applicationDbContext.SellActionLogs
            .Where(sal => sal.TradingDate >= startHistoryUtc && sal.TradingDate <= endHistoryUtc && sal.TradeAdvice.Contains("FireSell"))
            .AsQueryable();

        if (isLongTimeframe.HasValue)
        {
            query = query.Where(sal => EF.Property<bool>(sal, LongTimeframeSignalConstants.IsLongTimeframeProperty) == isLongTimeframe.Value);
        }

        var result = await query
            .OrderByDescending(x => x.TradingDate)
            .ToListAsync(cancellationToken: cancellationToken);

        return new ReadOnlyCollection<SellActionLog>(result);
    }

    public async Task<IReadOnlyCollection<SellActionLog>> GetSellActionLogs (DateTime startHistory, DateTime endHistory, bool? isLongTimeframe, CancellationToken cancellationToken, bool acceptableOnly = false, bool validatedOnly = true)
    {
        var applicationDbContext = serviceScopeFactory.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (applicationDbContext.SellActionLogs is null)
        {
            throw new InvalidOperationException ("Database column is null");
        }

        var startHistoryUtc = startHistory.ToUtcDateTime();
        var endHistoryUtc = endHistory.ToUtcDateTime();

        // Für Chart-Marker keine Navigationen laden: reduziert Payload und Laufzeit massiv.
        var query = applicationDbContext.SellActionLogs
            .Where(sal => sal.TradingDate >= startHistoryUtc && sal.TradingDate <= endHistoryUtc)
            .AsNoTracking()
            .AsQueryable();

        if (isLongTimeframe.HasValue)
        {
            query = query.Where(sal => EF.Property<bool>(sal, LongTimeframeSignalConstants.IsLongTimeframeProperty) == isLongTimeframe.Value);
        }

        if (acceptableOnly)
        {
            query = query.Where(sal => sal.TradeAdvice == "Sell");
        }

        if (validatedOnly)
        {
            query = query.Where(sal => sal.IsValidated);
        }

        var result = await query
            .OrderByDescending(x => x.TradingDate)
            .ToListAsync(cancellationToken: cancellationToken);

        return new ReadOnlyCollection<SellActionLog>(result);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<SellActionLog>> RelabelFireSellToSellActionLogs(DateTime startHistory, DateTime endHistory, CancellationToken cancellationToken)
    {
        var applicationDbContext = serviceScopeFactory.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (applicationDbContext.SellActionLogs is null)
        {
            throw new InvalidOperationException("Database column is null");
        }
        if (!applicationDbContext.SellActionLogs.Any())
        {
            throw new InvalidDataException("No data in database found!");
        }
        var startHistoryUtc = startHistory.ToUtcDateTime();
        var endHistoryUtc = endHistory.ToUtcDateTime();

        // Abfrage aller SellActionLogs sortiert nach TradingDate absteigend
        var fireSellActionLogs = applicationDbContext.SellActionLogs
            .Where(sal => sal.TradingDate >= startHistoryUtc && sal.TradingDate <= endHistoryUtc && sal.TradeAdvice.Contains("FireSell"))
            .ToList();

        foreach (var fireSellActionLog in fireSellActionLogs)
        {
            if(fireSellActionLog.TradeAdvice.Contains("FireSell"))
            {
                fireSellActionLog.TradeAdvice = fireSellActionLog.TradeAdvice.Replace("FireSell", "Sell");
            }
        }

        await applicationDbContext.SaveChangesAsync(cancellationToken);

        return new ReadOnlyCollection<SellActionLog>(fireSellActionLogs);
    }
}
