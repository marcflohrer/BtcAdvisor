using BtcAdvisor.Controllers.Models;

namespace BtcAdvisor.Constants;

public static class BitcoinConstants
{
    public static readonly IDictionary<string, int> MapPeriodToMinutes = new Dictionary<string, int>()
    {
        { "1w",  10080 },
        { "1d",  1440 },
        { "12h", 720 },
        { "4h",  240 },
        { "2h",  120 },
        { "1h",  60 },
        { "30m", 30 },
        { "15m", 15 },
        { "5m",  5 },
    };

    public static Dictionary<string, string> EmaMapToShortTermIndicator =>
        new ()
        {
            { "binance_BTC/USDT_15m_ema_20_0", "binance_BTC/USDT_15m_ema_10_0" }, 
            { "binance_BTC/USDT_15m_ema_50_0", "binance_BTC/USDT_15m_ema_20_0" }, 
            { "binance_BTC/USDT_15m_ema_100_0", "binance_BTC/USDT_15m_ema_50_0" }, 
            { "binance_BTC/USDT_15m_ema_200_0", "binance_BTC/USDT_15m_ema_100_0" },
            { "binance_BTC/USDT_5m_ema_20_0", "binance_BTC/USDT_5m_ema_10_0" }, 
            { "binance_BTC/USDT_5m_ema_50_0", "binance_BTC/USDT_5m_ema_20_0" }, 
            { "binance_BTC/USDT_5m_ema_100_0", "binance_BTC/USDT_5m_ema_50_0" }, 
            { "binance_BTC/USDT_5m_ema_200_0", "binance_BTC/USDT_5m_ema_100_0" },
            { "binance_BTC/USDT_30m_ema_20_0", "binance_BTC/USDT_30m_ema_10_0" }, 
            { "binance_BTC/USDT_30m_ema_50_0", "binance_BTC/USDT_30m_ema_20_0" }, 
            { "binance_BTC/USDT_30m_ema_100_0", "binance_BTC/USDT_30m_ema_50_0" }, 
            { "binance_BTC/USDT_30m_ema_200_0", "binance_BTC/USDT_30m_ema_100_0" },
            { "binance_BTC/USDT_1h_ema_20_0", "binance_BTC/USDT_1h_ema_10_0" }, 
            { "binance_BTC/USDT_1h_ema_50_0", "binance_BTC/USDT_1h_ema_20_0" }, 
            { "binance_BTC/USDT_1h_ema_100_0", "binance_BTC/USDT_1h_ema_50_0" }, 
            { "binance_BTC/USDT_1h_ema_200_0", "binance_BTC/USDT_1h_ema_100_0" },
            { "binance_BTC/USDT_2h_ema_20_0", "binance_BTC/USDT_2h_ema_10_0" }, 
            { "binance_BTC/USDT_2h_ema_50_0", "binance_BTC/USDT_2h_ema_20_0" }, 
            { "binance_BTC/USDT_2h_ema_100_0", "binance_BTC/USDT_2h_ema_50_0" }, 
            { "binance_BTC/USDT_2h_ema_200_0", "binance_BTC/USDT_2h_ema_100_0" },
            { "binance_BTC/USDT_4h_ema_20_0", "binance_BTC/USDT_4h_ema_10_0" }, 
            { "binance_BTC/USDT_4h_ema_50_0", "binance_BTC/USDT_4h_ema_20_0" }, 
            { "binance_BTC/USDT_4h_ema_100_0", "binance_BTC/USDT_4h_ema_50_0" }, 
            { "binance_BTC/USDT_4h_ema_200_0", "binance_BTC/USDT_4h_ema_100_0" },
            { "binance_BTC/USDT_12h_ema_20_0", "binance_BTC/USDT_12h_ema_10_0" }, 
            { "binance_BTC/USDT_12h_ema_50_0", "binance_BTC/USDT_12h_ema_20_0" }, 
            { "binance_BTC/USDT_12h_ema_100_0", "binance_BTC/USDT_12h_ema_50_0" }, 
            { "binance_BTC/USDT_12h_ema_200_0", "binance_BTC/USDT_12h_ema_100_0" },
            { "binance_BTC/USDT_1d_ema_20_0", "binance_BTC/USDT_1d_ema_10_0" }, 
            { "binance_BTC/USDT_1d_ema_50_0", "binance_BTC/USDT_1d_ema_20_0" }, 
            { "binance_BTC/USDT_1d_ema_100_0", "binance_BTC/USDT_1d_ema_50_0" }, 
            { "binance_BTC/USDT_1d_ema_200_0", "binance_BTC/USDT_1d_ema_100_0" },
            { "binance_BTC/USDT_1w_ema_20_0", "binance_BTC/USDT_1w_ema_10_0" }, 
            { "binance_BTC/USDT_1w_ema_50_0", "binance_BTC/USDT_1w_ema_20_0" }, 
            { "binance_BTC/USDT_1w_ema_100_0", "binance_BTC/USDT_1w_ema_50_0" }, 
            { "binance_BTC/USDT_1w_ema_200_0", "binance_BTC/USDT_1w_ema_100_0" },
        };

    public static List<IndicatorSpec> IndicatorList () => [
        new ("price", null),
        new ("rsi", 14),
        new ("stoch", null),
        new ("cci", null),
        new ("adx", null),
        new ("ao", null),
        new ("mom", null),
        new ("macd", null),
        new ("stochf", null),
        new ("willr", null),
        new ("kickingbylength", null),
        new ("ema", 10, false),
        new ("ema", 20, false),
        new ("ema", 50, false),
        new ("ema", 100, false),
        new ("ema", 200, false),
        new ("sma", 10, false),
        new ("sma", 20, false),
        new ("sma", 50, false),
        new ("sma", 100, false),
        new ("sma", 200, false),
        new ("ichimoku", null, false),
        new ("hma", 9, false),
    ];

    /// <summary>
    /// Known Bitcoin halving block-event timestamps (UTC, ascending).
    /// Used by the cycle-top post-halving window gate to suppress premature
    /// DistributionSell triggers that occur outside the historical cycle-top
    /// window. Historical cycle tops occurred 12–18 months after each halving
    /// (2013-12, 2017-12, 2021-11). Extend this list with future halvings
    /// (next expected ≈ April 2028) as they occur.
    /// </summary>
    public static readonly IReadOnlyList<DateTime> Halvings =
    [
        new DateTime(2012, 11, 28, 19, 24, 0, DateTimeKind.Utc),
        new DateTime(2016,  7,  9, 16, 46, 0, DateTimeKind.Utc),
        new DateTime(2020,  5, 11, 19, 23, 0, DateTimeKind.Utc),
        new DateTime(2024,  4, 19, 12,  9, 0, DateTimeKind.Utc),
    ];

    /// <summary>
    /// Returns true when <paramref name="asOfUtc"/> sits inside the post-halving
    /// window defined by [<paramref name="minMonthsAfterHalving"/>, <paramref name="maxMonthsAfterHalving"/>]
    /// relative to the most recent halving prior to <paramref name="asOfUtc"/>.
    /// When both bounds are &lt;= 0 the gate is treated as disabled (always true).
    /// When no halving precedes <paramref name="asOfUtc"/>, the gate is fail-open (true).
    /// Months are computed using 30.44-day month approximation for stable comparison.
    /// </summary>
    public static bool IsWithinPostHalvingWindow(DateTime asOfUtc, int minMonthsAfterHalving, int maxMonthsAfterHalving)
    {
        if (minMonthsAfterHalving <= 0 && maxMonthsAfterHalving <= 0)
        {
            return true;
        }

        var asOf = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        DateTime? lastHalving = null;
        foreach (var h in Halvings)
        {
            if (h <= asOf && (lastHalving is null || h > lastHalving.Value))
            {
                lastHalving = h;
            }
        }

        if (lastHalving is null)
        {
            return true;
        }

        var months = (asOf - lastHalving.Value).TotalDays / 30.44;
        if (minMonthsAfterHalving > 0 && months < minMonthsAfterHalving)
        {
            return false;
        }
        if (maxMonthsAfterHalving > 0 && months > maxMonthsAfterHalving)
        {
            return false;
        }
        return true;
    }
}
