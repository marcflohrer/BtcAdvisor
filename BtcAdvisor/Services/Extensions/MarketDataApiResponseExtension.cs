using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Extensions;

public static class MarketDataApiResponseExtensions
{
    public static IReadOnlyCollection<StockCandle> ToDatabaseModel(this MarketDataApiResponse response, string symbol)
    {
        ArgumentNullException.ThrowIfNull(response, nameof(response));
        var numberOfCandles = response.O.Count;
        if(response.H.Count != numberOfCandles ||
            response.L.Count != numberOfCandles ||
            response.C.Count != numberOfCandles ||
            response.V.Count != numberOfCandles ||
            response.T.Count != numberOfCandles
        )
        {
            throw new InvalidDataException("MarketData ApiResponse returned inconsistent array lengths.");
        }

        var stockCandles = new List<StockCandle>();
        for (var i = 0; i < numberOfCandles; ++i)
        {
            stockCandles.Add(new StockCandle
            {
                Symbol = symbol,
                OpenPrice = (decimal)response.O[i],
                HighPrice = (decimal)response.H[i],
                LowPrice = (decimal)response.L[i],
                ClosePrice = (decimal)response.C[i],
                Volume = response.V[i],
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(response.T[i]).UtcDateTime // Conversion from Unix timestamp
            });
        }

        return stockCandles.AsReadOnly();
    }
}