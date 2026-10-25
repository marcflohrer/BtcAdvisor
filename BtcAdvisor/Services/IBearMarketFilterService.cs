namespace BtcAdvisor.Services;

public interface IBearMarketFilterService
{
    /// <summary>
    /// Returns true when the asset is in a long-term bear regime as of the given timestamp.
    /// Definition: latest close on <paramref name="interval"/> is below the SMA(period) of closes
    /// up to (and including) the same timestamp. When data is insufficient, returns false
    /// (fail-open: do not block trading).
    /// </summary>
    Task<bool> IsBearMarketAsync(string interval, DateTime asOfUtc, int period, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when the asset is at a deep-cycle low as of <paramref name="asOfUtc"/>:
    /// the latest candle low has dropped at least <paramref name="drawdownThresholdPercent"/> percent
    /// below the all-time-high (high price) of the series up to that date.
    /// Example: drawdownThresholdPercent = 75.0 triggers when price is ≥ 75 % below ATH.
    /// Returns false when fewer candles are available than needed for a meaningful ATH.
    /// </summary>
    Task<bool> IsDeepCycleLowAsync(string interval, DateTime asOfUtc, double drawdownThresholdPercent, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when price is in a potential cycle-top overvaluation zone as of <paramref name="asOfUtc"/>:
    /// the latest candle high is close to cycle ATH and has already advanced far enough from cycle low.
    /// This signal is used as a sell-probability boost (not as an unconditional emergency exit).
    /// </summary>
    Task<bool> IsCycleTopOvervaluedAsync(
        string interval,
        DateTime asOfUtc,
        int lookbackDays,
        double minDrawupFromCycleLowPercent,
        double minAthProximityPercent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when the Pi Cycle Top indicator fires as of <paramref name="asOfUtc"/>:
    /// the 111-day SMA of closes has crossed above or reached the 350-day SMA of closes × 2.
    /// Historically this crossover has occurred within days of major Bitcoin cycle peaks (2013, 2017, 2021).
    /// Returns false when fewer than 350 candles are available.
    /// </summary>
    Task<bool> IsPiCycleTopAsync(string interval, DateTime asOfUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when the approximate Puell Multiple (daily close / SMA(365) of closes)
    /// is below <paramref name="threshold"/> as of <paramref name="asOfUtc"/>.
    /// A value below 0.5 historically marks deep-cycle lows with asymmetric upside.
    /// Fail-open (returns false) when fewer than 365 candles are available.
    /// </summary>
    Task<bool> IsPuellMultipleUndervaluedAsync(string interval, DateTime asOfUtc, double threshold, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when the current close price is above <paramref name="maxAthProximityPercent"/>
    /// percent of the running all-time high (ATH of all candle highs up to <paramref name="asOfUtc"/>),
    /// AND the ATH was set within the last <paramref name="maxAthAgeDays"/> days.
    /// If <paramref name="maxAthAgeDays"/> is 0 the age constraint is disabled (gate always active).
    /// A return value of true means a new buy should be blocked.
    /// Fail-open: returns false when fewer than 2 candles are available.
    /// </summary>
    Task<bool> IsAthProximityTooHighForBuyAsync(string interval, DateTime asOfUtc, double maxAthProximityPercent, int maxAthAgeDays, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when the current close price is at or above <paramref name="minAthProximityPercent"/>
    /// percent of the running all-time high as of <paramref name="asOfUtc"/>.
    /// Used to suppress regular Sell signals during ongoing bull runs near the ATH.
    /// Fail-open: returns false when fewer than 2 candles are available.
    /// </summary>
    Task<bool> IsNearAthForSellSuppressAsync(string interval, DateTime asOfUtc, double minAthProximityPercent, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the cycle all-time high (max candle high) within the rolling lookback window
    /// ending at <paramref name="asOfUtc"/>. Used by the ATH-breakout re-entry gate to
    /// determine the ATH level that must be exceeded by 1 % before re-entry is allowed.
    /// Returns 0.0 when fewer than 2 candles are available.
    /// </summary>
    Task<double> GetCycleAthAsync(string interval, DateTime asOfUtc, int lookbackDays, CancellationToken cancellationToken);
}
