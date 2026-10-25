using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public interface IBitcoinTradeAdvisor
{
    public Task<IReadOnlyCollection<BitcoinTradeStatus>> GetBitcoinTradeAdvice(
        DateTime startHistory,
        DateTime endHistory,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? strategyHigh,
        decimal? strategyLow,
        BuyActionLog? lowestBuyAfterFireSell,
        BuyActionLog? highestPanicBuyAfterLowestBuyAfterFireSell,
        CancellationToken cancellationToken);

    public Task<IReadOnlyCollection<BitcoinTradeStatus>> GetBitcoinTradeAdvice(
        DateTime startHistory,
        DateTime endHistory,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? strategyHigh,
        decimal? strategyLow,
        BuyActionLog? lowestBuyAfterFireSell,
        BuyActionLog? highestPanicBuyAfterLowestBuyAfterFireSell,
        CancellationToken cancellationToken,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet);

    public Task<List<(string TradeAdvice, decimal Price, DateTime TimeStamp)>> GetHistoryAsync(int take, CancellationToken cancellationToken);
}
