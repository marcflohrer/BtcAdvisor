using BtcAdvisor.Services.Conditions;

namespace BtcAdvisor.Services;

/// <summary>
/// In-memory per-position state that the caller must maintain and update
/// on every price tick while a position is open.
/// </summary>
public sealed class DistributionTopState
{
    /// <summary>Minimum price seen since the position was entered.</summary>
    public decimal? MinPriceSinceEntry { get; set; }

    /// <summary>Maximum price seen since the position was entered.</summary>
    public decimal? MaxPriceSinceEntry { get; set; }

    /// <summary>Gain (%) of the last fully completed buy→sell cycle, or null if no prior cycle.</summary>
    public double? PreviousCycleGainPercent { get; set; }

    /// <summary>Updates the running min/max with the given tick price.</summary>
    public void Tick(decimal price)
    {
        if (MinPriceSinceEntry == null || price < MinPriceSinceEntry)
            MinPriceSinceEntry = price;
        if (MaxPriceSinceEntry == null || price > MaxPriceSinceEntry)
            MaxPriceSinceEntry = price;
    }

    /// <summary>Resets per-cycle state when a new position is entered.</summary>
    public void OnEntry(decimal entryPrice)
    {
        MinPriceSinceEntry = entryPrice;
        MaxPriceSinceEntry = entryPrice;
    }

    /// <summary>Records the completed cycle gain so the next cycle can compare against it.</summary>
    public void OnExit(decimal entryPrice, decimal exitPrice)
    {
        if (entryPrice > 0)
            PreviousCycleGainPercent = (double)((exitPrice - entryPrice) / entryPrice * 100m);
        MinPriceSinceEntry = null;
        MaxPriceSinceEntry = null;
    }
}

/// <summary>
/// Evaluates two distribution-top sell concepts that complement the primary
/// bearish-confluence gate.
/// </summary>
public static class DistributionTopFilter
{
    /// <summary>
    /// Concept A – Momentum brake.
    /// Returns true when there is a prior cycle to compare against and the
    /// current cycle's gain has deccelerated below the configured ratio.
    /// </summary>
    public static bool IsMomentumBrake(
        decimal entryPrice,
        decimal currentPrice,
        DistributionTopState state,
        double minCycleGainPercent = DistributionTopThresholds.MomentumBrakeMinCycleGainPercent,
        double maxRatio = DistributionTopThresholds.MomentumBrakeMaxRatio)
    {
        if (state.PreviousCycleGainPercent is not { } prevGain || prevGain <= 0)
            return false;

        var currentGain = entryPrice > 0
            ? (double)((currentPrice - entryPrice) / entryPrice * 100m)
            : 0.0;

        if (currentGain < minCycleGainPercent)
            return false;

        return currentGain / prevGain < maxRatio;
    }

    /// <summary>
    /// Concept B – Intermediate trough.
    /// Returns true when the position has experienced a significant intra-cycle
    /// drawdown from the cycle peak, and price has since recovered to within
    /// proximity of the cycle high.
    /// </summary>
    public static bool IsIntermediateTrough(
        decimal entryPrice,
        decimal currentPrice,
        DistributionTopState state,
        double minCycleGainPercent = DistributionTopThresholds.IntermediateTroughMinCycleGainPercent,
        double minDrawdownPercent = DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
        double recoveryProximityPercent = DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent)
    {
        if (state.MinPriceSinceEntry is not { } minPrice
            || state.MaxPriceSinceEntry is not { } maxPrice
            || maxPrice <= 0 || entryPrice <= 0)
            return false;

        // Must be profitable
        var currentGain = (double)((currentPrice - entryPrice) / entryPrice * 100m);
        if (currentGain < minCycleGainPercent)
            return false;

        // Significant drawdown from cycle peak
        var drawdownFromPeak = (double)((maxPrice - minPrice) / maxPrice * 100m);
        if (drawdownFromPeak < minDrawdownPercent)
            return false;

        // Price has recovered near the cycle high
        var proximityToPeak = (double)((maxPrice - currentPrice) / maxPrice * 100m);
        return proximityToPeak <= recoveryProximityPercent;
    }

    /// <summary>
    /// Returns true if either distribution-top concept fires.
    /// </summary>
    public static bool ShouldTriggerDistributionSell(
        decimal entryPrice,
        decimal currentPrice,
        DistributionTopState state,
        double momentumBrakeMinCycleGainPercent = DistributionTopThresholds.MomentumBrakeMinCycleGainPercent,
        double momentumBrakeMaxRatio = DistributionTopThresholds.MomentumBrakeMaxRatio,
        double troughMinCycleGainPercent = DistributionTopThresholds.IntermediateTroughMinCycleGainPercent,
        double troughMinDrawdownPercent = DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
        double troughRecoveryProximityPercent = DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent)
    {
        return IsMomentumBrake(entryPrice, currentPrice, state, momentumBrakeMinCycleGainPercent, momentumBrakeMaxRatio)
            || IsIntermediateTrough(entryPrice, currentPrice, state, troughMinCycleGainPercent, troughMinDrawdownPercent, troughRecoveryProximityPercent);
    }
}
