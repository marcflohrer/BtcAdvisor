namespace BtcAdvisor.Services.Conditions;

/// <summary>
/// Thresholds for the distribution-top sell gate.
///
/// Concept A – Momentum brake: A sell fires when the current cycle's gain
/// from entry is significantly smaller (as a ratio) than the previous
/// completed cycle's gain.  Indicates decelerating bull-market momentum.
///
/// Concept B – Intermediate trough: A sell fires when, after entry, the
/// price formed a significant intra-cycle low and has since recovered back
/// near the cycle high.  Indicates a classic distribution / re-test top.
///
/// Both are implemented as additional sell triggers in the backtest loop.
/// Set either ratio/percent to a very large value to disable that sub-filter.
/// </summary>
public static class DistributionTopThresholds
{
    // ── Concept A: Momentum brake ──────────────────────────────────────────

    /// <summary>
    /// Minimum current-cycle gain (%) required before a momentum-brake sell
    /// can fire.  Avoids triggering on tiny bounces.
    /// </summary>
    public const double MomentumBrakeMinCycleGainPercent = 10.0;

    /// <summary>
    /// If the current cycle's gain is less than this fraction of the
    /// previous completed cycle's gain, a momentum-brake sell is generated.
    /// E.g. 0.40 means "current gain &lt; 40 % of previous gain → sell."
    /// Set to 0.0 to disable concept A entirely.
    /// ADR-006: Optimal historical performance requires this set to 0.0 (concept A disabled).
    /// </summary>
    public const double MomentumBrakeMaxRatio = 0.0;

    // ── Concept B: Intermediate trough ────────────────────────────────────

    /// <summary>
    /// Minimum intra-cycle drawdown (%) from the cycle peak that qualifies
    /// as an "intermediate trough."
    /// Set to a very large value (e.g. 999.0) to disable concept B entirely.
    /// ADR-006: Optimal historical performance requires this set to 999.0 (concept B disabled).
    /// </summary>
    public const double IntermediateTroughMinDrawdownPercent = 999.0;

    /// <summary>
    /// Minimum current-cycle gain (%) from entry required before an
    /// intermediate-trough sell can fire.  Ensures we are at least at a
    /// profit before exiting.
    /// </summary>
    public const double IntermediateTroughMinCycleGainPercent = 10.0;

    /// <summary>
    /// After the trough, price must have recovered to within this percentage
    /// of the cycle peak for the trough-sell to fire.  E.g. 5.0 means price
    /// must be within 5 % of the all-time cycle high.
    /// Set to a very large value (e.g. 999.0) to disable concept B entirely.
    /// ADR-006: Optimal historical performance requires this set to 999.0 (concept B disabled).
    /// </summary>
    public const double IntermediateTroughRecoveryProximityPercent = 999.0;
}
