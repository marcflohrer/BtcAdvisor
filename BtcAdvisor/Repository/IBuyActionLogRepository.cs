using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Repository;

public interface IBuyActionLogRepository
{
    public Task<IReadOnlyCollection<BuyActionLog>> GetBuyActionLogs(DateTime startHistory, DateTime endHistory, bool? isLongTimeframe, CancellationToken cancellationToken, bool acceptableOnly = false, bool validatedOnly = true);
    
    public Task<BuyActionLog?> GetLowestBuyPriceSinceLatestFireSell(DateTime currentDateTime, CancellationToken cancellationToken);

    public Task<BuyActionLog?> GetHighestPanicBuyPriceAfterLowestBuyPriceSinceLatestFireSell(DateTime currentDateTime, CancellationToken cancellationToken);
}
