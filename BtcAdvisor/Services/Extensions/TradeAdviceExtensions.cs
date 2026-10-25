using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Extensions;

public static class TradeAdviceExtensions
{
    public static TradeAdvice? ParseTradeAdvice(string tradeAdvice)
    {
        if (Enum.TryParse<TradeAdvice>(tradeAdvice, true, out var parsedAdvice))
        {
            return parsedAdvice;
        }
        return null;
    }
}
