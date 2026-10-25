using Microsoft.EntityFrameworkCore.Storage;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Repository;

public interface ITradingRepository
{
    Task SaveBuyActionLog(BuyActionLog buyActionLog, CancellationToken cancellationToken);

    Task SaveSellActionLog(SellActionLog sellActionLog, CancellationToken cancellationToken);

    Task<BuyActionLog?> FindLatestBuyAction(DateTime currentDateTime);

    Task<SellActionLog?> FindLatestFireSellAction(DateTime currentDateTime);
    Task<BuyActionLog?> FindLatestPanicBuyAction(DateTime currentDateTime);

    Task<SellActionLog?> FindLatestSellAction(DateTime currentDateTime);

    Task<(SellActionLog?, BuyActionLog?)> FindLatestAction(DateTime currentDateTime);

    Task<IReadOnlyCollection<BuyActionLog>> FindStrongBuyLogs(DateTime periodStart);

    Task UpdateBuyActionLog(BuyActionLog buyActionLog, CancellationToken cancellationToken);

    Task DeleteSellBuyActionLogsInRange(DateTime startDate, DateTime endDate, CancellationToken cancellationToken);

    Task<int> CountBuyActionLogsInRange(DateTime startDate, DateTime endDate, CancellationToken cancellationToken);

    Task<int> CountSellActionLogsInRange(DateTime startDate, DateTime endDate, CancellationToken cancellationToken);
    
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}