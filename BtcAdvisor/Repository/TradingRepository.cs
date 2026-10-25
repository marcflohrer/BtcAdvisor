using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using BtcAdvisor.Extensions;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Repository;

public class TradingRepository(ApplicationDbContext applicationDbContext) : ITradingRepository
{
    private readonly ApplicationDbContext applicationDbContext = applicationDbContext;

    public async Task SaveBuyActionLog(BuyActionLog buyActionLog, CancellationToken cancellationToken)
    {
        // check if this is a duplicate buy action 
        var isDuplicate = applicationDbContext.BuyActionLogs
            .Any(b => b.TradingDate.CompareTo(buyActionLog.TradingDate) == 0 
                && b.TradeAdvice == buyActionLog.TradeAdvice
                && b.Price == buyActionLog.Price);
        if (isDuplicate)
        {
            return;
        }

        applicationDbContext.BuyActionLogs.Add(buyActionLog);
        applicationDbContext.Entry(buyActionLog)
            .Property<bool>(LongTimeframeSignalConstants.IsLongTimeframeProperty)
            .CurrentValue = LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe;
        await applicationDbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveSellActionLog(SellActionLog sellActionLog, CancellationToken cancellationToken)
    {
        // check if this is a duplicate sell action
        var isDuplicate = applicationDbContext.SellActionLogs
            .Any(s => s.TradingDate == sellActionLog.TradingDate && s.TradeAdvice == sellActionLog.TradeAdvice && s.Price == sellActionLog.Price);
        if (isDuplicate)
        {
            return;
        }
        applicationDbContext.SellActionLogs.Add(sellActionLog);
        applicationDbContext.Entry(sellActionLog)
            .Property<bool>(LongTimeframeSignalConstants.IsLongTimeframeProperty)
            .CurrentValue = LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe;
        await applicationDbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<BuyActionLog?> FindLatestBuyAction(DateTime currentDateTime)
    {
        return await applicationDbContext.BuyActionLogs
            .Where(bal => bal.TradingDate <= currentDateTime.ToUniversalTime())
            .Where(s => s.TradeAdvice.Contains("Buy"))
            .Where(b => EF.Property<bool>(b, LongTimeframeSignalConstants.IsLongTimeframeProperty) == LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe)
            .Where(b => b.IsValidated)
            .OrderByDescending(buyActionLog => buyActionLog.TradingDate)
            .FirstOrDefaultAsync();
    }

    public async Task<SellActionLog?> FindLatestSellAction(DateTime currentDateTime)
    {
        return await applicationDbContext.SellActionLogs
            .Include(s => s.BuyActionLog)
            .Where(sal => sal.TradingDate < currentDateTime.ToUniversalTime())
            .Where(s => s.TradeAdvice.Contains("Sell") || s.TradeAdvice == "HardStopLoss")
            .Where(s => EF.Property<bool>(s, LongTimeframeSignalConstants.IsLongTimeframeProperty) == LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe)
            .OrderByDescending(s => s.TradingDate)
            .FirstOrDefaultAsync();
    }

    public async Task<SellActionLog?> FindLatestFireSellAction(DateTime currentDateTime)
    {
        return await applicationDbContext.SellActionLogs
            .Where(sal => sal.TradingDate < currentDateTime.ToUniversalTime())
            .Where(s => s.TradeAdvice.Contains("FireSell"))
            .Where(s => EF.Property<bool>(s, LongTimeframeSignalConstants.IsLongTimeframeProperty) == LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe)
            .OrderByDescending(s => s.TradingDate)
            .FirstOrDefaultAsync();
    }

    public async Task<(SellActionLog?, BuyActionLog?)> FindLatestAction(DateTime currentDateTime)
    {
        var sellAction = await FindLatestSellAction(currentDateTime);
        var buyAction = await FindLatestBuyAction(currentDateTime);
        return (sellAction, buyAction);
    }

    public async Task<IReadOnlyCollection<BuyActionLog>> FindStrongBuyLogs(DateTime periodStart)
    {
        periodStart = periodStart.ToUtcDateTime();
        var result = await applicationDbContext.BuyActionLogs
            .Where(log => log!.SellActionLogs!.Any() && log!.TradeAdvice == TradeAdvice.StrongBuy.ToString() && log.TradingDate > periodStart)
            .OrderByDescending(log => log.TradingDate)
            .AsNoTracking()
            .ToListAsync();
        return result;
    }

    public async Task UpdateBuyActionLog(BuyActionLog buyActionLog, CancellationToken cancellationToken)
    {
        applicationDbContext.BuyActionLogs.Update(buyActionLog);
        await applicationDbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Löscht alle BuyActionLogs und SellActionLogs in einem bestimmten Zeitbereich.
    /// </summary>
    public async Task DeleteSellBuyActionLogsInRange(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        // 1. Delete all SellActionLogs in the given range
        var sellActionLogsToDelete = await applicationDbContext.SellActionLogs
            .Where(s => s.TradingDate >= startDate.ToUniversalTime() && s.TradingDate <= endDate.ToUniversalTime())
            .ToListAsync(cancellationToken);

        if (sellActionLogsToDelete.Any())
        {
            applicationDbContext.SellActionLogs.RemoveRange(sellActionLogsToDelete);
        }

        // 2. Delete all BuyActionLogs in the given range
        var buyActionLogsToDelete = await applicationDbContext.BuyActionLogs
            .Where(b => b.TradingDate >= startDate.ToUniversalTime() && b.TradingDate <= endDate.ToUniversalTime())
            .ToListAsync(cancellationToken);

        if (buyActionLogsToDelete.Any())
        {
            applicationDbContext.BuyActionLogs.RemoveRange(buyActionLogsToDelete);
        }

        // 3. Delete ActionLogs in the given range
        var countActionLogsToDelete = await (applicationDbContext
            .ActionLogs?
            .CountAsync(a => a.TradingDate >= startDate.ToUniversalTime() && a.TradingDate <= endDate.ToUniversalTime(), cancellationToken) 
            ?? Task.FromResult(0));
        if (countActionLogsToDelete != 0)
        {
            var actionLogsToDelete = await applicationDbContext.ActionLogs!
                .Where(a => a.TradingDate >= startDate.ToUniversalTime() && a.TradingDate <= endDate.ToUniversalTime())
                .ToListAsync(cancellationToken);
            applicationDbContext.ActionLogs!.RemoveRange(actionLogsToDelete);
        }

        // 4. Save changes to the database
        await applicationDbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountBuyActionLogsInRange(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        var startUtc = startDate.ToUniversalTime();
        var endUtc = endDate.ToUniversalTime();
        return applicationDbContext.BuyActionLogs
            .Where(b => b.TradingDate >= startUtc && b.TradingDate <= endUtc)
            .Where(b => EF.Property<bool>(b, LongTimeframeSignalConstants.IsLongTimeframeProperty) == LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe)
            .CountAsync(cancellationToken);
    }

    public Task<int> CountSellActionLogsInRange(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        var startUtc = startDate.ToUniversalTime();
        var endUtc = endDate.ToUniversalTime();
        return applicationDbContext.SellActionLogs
            .Where(s => s.TradingDate >= startUtc && s.TradingDate <= endUtc)
            .Where(s => EF.Property<bool>(s, LongTimeframeSignalConstants.IsLongTimeframeProperty) == LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe)
            .CountAsync(cancellationToken);
    }

    /// <summary>
    /// Startet eine Datenbank-Transaktion.
    /// </summary>
    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Findet die neueste BuyActionLog mit "PanicBuy" im TradeAdvice, die vor dem angegebenen Datum liegt.
    /// </summary>
    /// <param name="currentDateTime"></param>
    /// <returns>BuyActionLog oder null, wenn keine passende Log gefunden wurde.</returns>
    public Task<BuyActionLog?> FindLatestPanicBuyAction(DateTime currentDateTime)
    {
        return applicationDbContext.BuyActionLogs
            .Where(sal => sal.TradingDate < currentDateTime.ToUniversalTime())
            .Where(s => s.TradeAdvice.Contains("PanicBuy"))
            .Where(b => EF.Property<bool>(b, LongTimeframeSignalConstants.IsLongTimeframeProperty) == LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe)
            .Where(b => b.IsValidated)
            .OrderByDescending(s => s.TradingDate)
            .FirstOrDefaultAsync();
    }
}
