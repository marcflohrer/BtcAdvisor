namespace BtcAdvisor.Services.Conditions;

/// <summary>
/// Empirically calibrated thresholds for the long-timeframe confluence evaluator.
/// Derived from the InsightsController error-rate sweep (2024+2025, with-1w profile,
/// priceTolerancePercent=10, extremaWindowDays=14, maxConflictingAnchorRanges=0).
///
/// Bullish optimum: T1=41.7%, T2=0%, ~12 signals/year
/// Bearish optimum: T1=52.6%, T2=0%, ~19 signals/year
///
/// See ADR-007 for rationale and evidence.
/// </summary>
public static class LongTimeframeThresholds
{
    /// <summary>Minimum number of LTF ranges that must be bullish-aligned (buy &gt; sell).</summary>
    public const int BullishMinAlignedRanges = 3;

    /// <summary>
    /// Maximum number of LTF ranges allowed to be bullish-aligned for a valid entry.
    /// Entries with AR above this threshold are rejected. Use <see cref="int.MaxValue"/> (default) to disable the cap.
    /// Signal-returns correlation (n=2042, 2017–2026) shows AR=3,TC≤1 has pos30d=62% vs AR=4 pos30d=52%.
    /// </summary>
    public const int BullishMaxAlignedRanges = int.MaxValue;

    /// <summary>Minimum number of anchor ranges (1d, 1w) that must be bullish-aligned.</summary>
    public const int BullishMinAlignedAnchorRanges = 2;

    /// <summary>Maximum number of anchor ranges allowed to be bearish-aligned while evaluating bullish.</summary>
    public const int BullishMaxConflictingAnchorRanges = 0;

    /// <summary>
    /// Minimum total strong+medium trend indicator count across all LTF ranges for a bullish signal.
    /// A value of 2 filters weak sideways confluence where buy&gt;sell is technically true but trend support is too thin.
    /// </summary>
    public const int BullishMinTrendCountSum = 1;

    /// <summary>
    /// Maximum total strong+medium trend indicator count allowed for a bullish entry.
    /// Entries with TC above this threshold are rejected. Use <see cref="int.MaxValue"/> (default) to disable the cap.
    /// Correlation analysis shows that TC≥2 entries cluster in late-cycle bubble phases with poor outcomes.
    /// </summary>
    public const int BullishMaxTrendCountSum = int.MaxValue;

    /// <summary>Minimum number of LTF ranges that must be bearish-aligned (sell &gt; buy).</summary>
    public const int BearishMinAlignedRanges = 3;

    /// <summary>Minimum number of anchor ranges (1d, 1w) that must be bearish-aligned.</summary>
    public const int BearishMinAlignedAnchorRanges = 0;

    /// <summary>Maximum number of anchor ranges allowed to be bullish-aligned while evaluating bearish.</summary>
    public const int BearishMaxConflictingAnchorRanges = 0;

    /// <summary>Minimum total strong+medium trend indicator count across all LTF ranges for a bearish signal.</summary>
    public const int BearishMinTrendCountSum = 2;
}
