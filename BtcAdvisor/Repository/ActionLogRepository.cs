using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;
using System.Linq;

namespace BtcAdvisor.Repository;

public class ActionLogRepository : IActionLogRepository
{
    private readonly ApplicationDbContext _context;

    public ActionLogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SaveActionLog(ActionLog actionLog)
    {
        // check if action log is already stored (to avoid duplicates)
        var isDuplicate = await _context.ActionLogs
            .AnyAsync(a => a.Type == actionLog.Type 
                && a.Price == actionLog.Price 
                && a.TradingDate.CompareTo(actionLog.TradingDate) == 0);
        if (isDuplicate)
        {
            return;
        }
        actionLog.TradingDate = actionLog.TradingDate.ToUniversalTime();
        actionLog.IsLongTimeframe = LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe;
        _context.Add(actionLog);
        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyCollection<ActionLog>> GetActionLogs(DateTime currentDateTime, CancellationToken cancellationToken)
    {
        // Schritt 1: GroupBy und Selektion der letzten Einträge pro Gruppe
        var buyLogIds = await _context.BuyActionLogs
            .Where(x => EF.Property<bool>(x, LongTimeframeSignalConstants.IsLongTimeframeProperty) == LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe)
            .Where(x => x.TradeAdvice.Contains("Panic"))
            .GroupBy(x => x.TradingDate.Date)
            .Select(g => g.OrderByDescending(x => x.Price).LastOrDefault()!.Id)
            .ToListAsync(cancellationToken);

        // Schritt 2: Abfrage der vollständigen Datensätze mit Projektion
        var buyLogs = await _context.BuyActionLogs
            .Where(x => buyLogIds.Contains(x.Id))
            .Select(x => new ActionLog
            {
                Type = "Buy",
                TradeAdvice = x.TradeAdvice,
                Price = x.Price,
                TradingDate = x.TradingDate,
                IsLongTimeframe = EF.Property<bool>(x, LongTimeframeSignalConstants.IsLongTimeframeProperty)
            })
            .ToListAsync(cancellationToken);

        // Analog für SellLogs
        var sellLogIds = await _context.SellActionLogs
            .Where(x => EF.Property<bool>(x, LongTimeframeSignalConstants.IsLongTimeframeProperty) == LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe)
            .Where(x => x.TradeAdvice.Contains("Fire"))
            .GroupBy(x => x.TradingDate.Date)
            .Select(g => g.OrderByDescending(x => x.Price).FirstOrDefault()!.Id)
            .ToListAsync(cancellationToken);

        var sellLogs = await _context.SellActionLogs
            .Where(x => sellLogIds.Contains(x.Id))
            .Select(x => new ActionLog
            {
                Type = "Sell",
                TradeAdvice = x.TradeAdvice,
                Price = x.Price,
                TradingDate = x.TradingDate,
                IsLongTimeframe = EF.Property<bool>(x, LongTimeframeSignalConstants.IsLongTimeframeProperty)
            })
            .ToListAsync(cancellationToken);

        var tradeLogs = buyLogs.Union(sellLogs);

        // Schritt 3: Deduplizierung benachbarter TradeAdvice-Werte
        IReadOnlyCollection<ActionLog> orderedTradeLogs = [..tradeLogs.OrderByDescending(x => x.TradingDate)];
        var deduplicatedLogs = new List<ActionLog>();
        string? lastTradeAdvice = null;
        foreach (var log in from log in orderedTradeLogs
                            where log.TradeAdvice != lastTradeAdvice
                            select log)
        {
            deduplicatedLogs.Add(log);
            lastTradeAdvice = log.TradeAdvice;
        }

        return deduplicatedLogs;
    }

    public async Task<ActionLog?> GetLastActionLog(string type, CancellationToken cancellationToken)
    {
        var result = await _context.ActionLogs
            .Where(a => a.Type == type)
            .OrderByDescending(a => a.TradingDate)
            .FirstOrDefaultAsync(cancellationToken);

        return result;
    }

    public async Task DeleteActionLogs(DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        // delete all action logs
        _context.ActionLogs.RemoveRange(_context.ActionLogs.Where(x => x.TradingDate > start.ToUniversalTime() && x.TradingDate < end.ToUniversalTime()));
        await _context.SaveChangesAsync(cancellationToken);
    }
}