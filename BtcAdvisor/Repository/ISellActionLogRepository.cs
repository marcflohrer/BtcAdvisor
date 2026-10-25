using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Repository;

public interface ISellActionLogRepository
{
    public Task<IReadOnlyCollection<SellActionLog>> GetSellActionLogs (DateTime startHistory, DateTime endHistory, bool? isLongTimeframe, CancellationToken cancellationToken, bool acceptableOnly = false, bool validatedOnly = true);
    public Task<IReadOnlyCollection<SellActionLog>> GetFireSellActionLogs (DateTime startHistory, DateTime endHistory, bool? isLongTimeframe, CancellationToken cancellationToken);
    
    /// <summary>
    /// This method is used to relabel FireSell action logs to regular Sell action logs. This is necessary because in some cases, a FireSell action might be incorrectly labeled as such due to timing issues. By relabeling them to regular Sell action logs, we can ensure that the data is more accurate and consistent for analysis and reporting purposes.
    /// </summary>
    /// <param name="startHistory">The start date.</param>
    /// <param name="endHistory">The end date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The relabeled fire sell actions.</returns>
    public Task<IReadOnlyCollection<SellActionLog>> RelabelFireSellToSellActionLogs (DateTime startHistory, DateTime endHistory, CancellationToken cancellationToken);
}
