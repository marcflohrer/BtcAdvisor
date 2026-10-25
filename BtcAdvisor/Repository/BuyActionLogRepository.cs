using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository.Models;
using static BtcAdvisor.Extensions.DateTimeExtensions;
using System.Collections.ObjectModel;

namespace BtcAdvisor.Repository;

public class BuyActionLogRepository (IServiceScopeFactory serviceScopeFactory) : IBuyActionLogRepository
{
    public async Task<IReadOnlyCollection<BuyActionLog>> GetBuyActionLogs (DateTime startHistory, DateTime endHistory, bool? isLongTimeframe, CancellationToken cancellationToken, bool acceptableOnly = false, bool validatedOnly = true)
    {
        var applicationDbContext = serviceScopeFactory.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (applicationDbContext.IndicatorData is null)
        {
            throw new InvalidOperationException ("Database column is null");
        }

        var startHistoryUtc = startHistory.ToUtcDateTime().ToUniversalTime();
        var endHistoryUtc = endHistory.ToUtcDateTime().ToUniversalTime();

        // Für Chart-Marker keine Navigationen laden: reduziert Payload und Laufzeit massiv.
        var query = applicationDbContext.BuyActionLogs
            .Where(bal => bal.TradingDate >= startHistoryUtc && bal.TradingDate <= endHistoryUtc)
            .AsNoTracking()
            .AsQueryable();

        if (isLongTimeframe.HasValue)
        {
            query = query.Where(bal => EF.Property<bool>(bal, LongTimeframeSignalConstants.IsLongTimeframeProperty) == isLongTimeframe.Value);
        }

        if (acceptableOnly)
        {
            query = query.Where(bal => bal.TradeAdvice == "Buy");
        }

        if (validatedOnly)
        {
            query = query.Where(bal => bal.IsValidated);
        }

        var result = await query
            .OrderByDescending(x => x.TradingDate)
            .ToListAsync(cancellationToken: cancellationToken);

        return new ReadOnlyCollection<BuyActionLog>(result);
    }

    public async Task<BuyActionLog?> GetLowestBuyPriceSinceLatestFireSell(DateTime currentDateTime, CancellationToken cancellationToken)
    {
        var applicationDbContext = serviceScopeFactory.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (applicationDbContext.IndicatorData is null)
        {
            return null;
        }

        // Prüfe, ob es BuyActionLogs gibt
        if (!await applicationDbContext.BuyActionLogs.AnyAsync(cancellationToken))
        {
            return null;
        }

        // Hole den neuesten "FireSell"-Eintrag
        var latestFireSell = await applicationDbContext.SellActionLogs
            .Where(sell => sell.TradeAdvice != null && sell.TradeAdvice.Contains("FireSell") && sell.TradingDate <= currentDateTime.ToUniversalTime())
            .OrderByDescending(x => x.TradingDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestFireSell == null)
        {
            return null;
        }

        // Hole den günstigsten Kauf seit dem letzten "FireSell"
        var lowestBuySinceLatestFireSell = await applicationDbContext.BuyActionLogs
            .Where(buy => buy.TradingDate > latestFireSell.TradingDate && buy.TradingDate <= currentDateTime.ToUniversalTime())
            .OrderBy(x => x.Price)
            .FirstOrDefaultAsync(cancellationToken);

        return lowestBuySinceLatestFireSell;
    }

    public async Task<BuyActionLog?> GetHighestPanicBuyPriceAfterLowestBuyPriceSinceLatestFireSell(DateTime currentDateTime, CancellationToken cancellationToken)
    {
        var applicationDbContext = serviceScopeFactory.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (applicationDbContext.IndicatorData is null)
        {
            return null;
        }

        // Prüfe, ob es BuyActionLogs gibt
        if (!await applicationDbContext.BuyActionLogs.AnyAsync(cancellationToken))
        {
            return null;
        }

        // Hole den neuesten "FireSell"-Eintrag
        var latestFireSell = await applicationDbContext.SellActionLogs
            .Where(sell => sell.TradeAdvice != null && sell.TradeAdvice.Contains("FireSell") && sell.TradingDate <= currentDateTime.ToUniversalTime())
            .OrderByDescending(x => x.TradingDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestFireSell == null)
        {
            return null;
        }

        // Hole den günstigsten Kauf seit dem letzten "FireSell"
        var lowestBuySinceLatestFireSell = await applicationDbContext.BuyActionLogs
            .Where(buy => buy.TradingDate > latestFireSell.TradingDate && buy.TradingDate <= currentDateTime.ToUniversalTime())
            .OrderBy(x => x.Price)
            .FirstOrDefaultAsync(cancellationToken);

        if (lowestBuySinceLatestFireSell == null)
        {
            return null;
        }

        var highestPanicBuyPriceAfterLowestBuySinceLatestFireSell = await applicationDbContext.BuyActionLogs
            .Where(buy => buy.TradeAdvice != null && buy.TradeAdvice.Contains("PanicBuy") && buy.TradingDate > lowestBuySinceLatestFireSell.TradingDate && buy.TradingDate <= currentDateTime.ToUniversalTime())
            .OrderByDescending(x => x.Price)
            .FirstOrDefaultAsync(cancellationToken);

        return highestPanicBuyPriceAfterLowestBuySinceLatestFireSell;
    }
}
