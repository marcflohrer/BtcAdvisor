namespace BtcAdvisor.Services.Conditions;

public sealed record class LongTimeframeConfluenceThresholdSet
{
    public static LongTimeframeConfluenceThresholdSet Default { get; } = new();

    public int BullishMinAlignedRanges { get; }
    public int BullishMaxAlignedRanges { get; }
    public int BullishMinAlignedAnchorRanges { get; }
    public int BullishMaxConflictingAnchorRanges { get; }
    public int BullishMinTrendCountSum { get; }
    public int BullishMaxTrendCountSum { get; }
    public int BearishMinAlignedRanges { get; }
    public int BearishMinAlignedAnchorRanges { get; }
    public int BearishMaxConflictingAnchorRanges { get; }
    public int BearishMinTrendCountSum { get; }

    public LongTimeframeConfluenceThresholdSet(
        int bullishMinAlignedRanges = LongTimeframeThresholds.BullishMinAlignedRanges,
        int bullishMaxAlignedRanges = LongTimeframeThresholds.BullishMaxAlignedRanges,
        int bullishMinAlignedAnchorRanges = LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
        int bullishMaxConflictingAnchorRanges = LongTimeframeThresholds.BullishMaxConflictingAnchorRanges,
        int bullishMinTrendCountSum = LongTimeframeThresholds.BullishMinTrendCountSum,
        int bullishMaxTrendCountSum = LongTimeframeThresholds.BullishMaxTrendCountSum,
        int bearishMinAlignedRanges = LongTimeframeThresholds.BearishMinAlignedRanges,
        int bearishMinAlignedAnchorRanges = LongTimeframeThresholds.BearishMinAlignedAnchorRanges,
        int bearishMaxConflictingAnchorRanges = LongTimeframeThresholds.BearishMaxConflictingAnchorRanges,
        int bearishMinTrendCountSum = LongTimeframeThresholds.BearishMinTrendCountSum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bullishMinAlignedRanges);
        if (bullishMaxAlignedRanges < bullishMinAlignedRanges)
            throw new ArgumentOutOfRangeException(nameof(bullishMaxAlignedRanges), "Must be >= bullishMinAlignedRanges.");
        ArgumentOutOfRangeException.ThrowIfNegative(bullishMinAlignedAnchorRanges);
        ArgumentOutOfRangeException.ThrowIfNegative(bullishMaxConflictingAnchorRanges);
        ArgumentOutOfRangeException.ThrowIfNegative(bullishMinTrendCountSum);
        if (bullishMaxTrendCountSum < bullishMinTrendCountSum)
            throw new ArgumentOutOfRangeException(nameof(bullishMaxTrendCountSum), "Must be >= bullishMinTrendCountSum.");
        ArgumentOutOfRangeException.ThrowIfNegative(bearishMinAlignedRanges);
        ArgumentOutOfRangeException.ThrowIfNegative(bearishMinAlignedAnchorRanges);
        ArgumentOutOfRangeException.ThrowIfNegative(bearishMaxConflictingAnchorRanges);
        ArgumentOutOfRangeException.ThrowIfNegative(bearishMinTrendCountSum);

        BullishMinAlignedRanges = bullishMinAlignedRanges;
        BullishMaxAlignedRanges = bullishMaxAlignedRanges;
        BullishMinAlignedAnchorRanges = bullishMinAlignedAnchorRanges;
        BullishMaxConflictingAnchorRanges = bullishMaxConflictingAnchorRanges;
        BullishMinTrendCountSum = bullishMinTrendCountSum;
        BullishMaxTrendCountSum = bullishMaxTrendCountSum;
        BearishMinAlignedRanges = bearishMinAlignedRanges;
        BearishMinAlignedAnchorRanges = bearishMinAlignedAnchorRanges;
        BearishMaxConflictingAnchorRanges = bearishMaxConflictingAnchorRanges;
        BearishMinTrendCountSum = bearishMinTrendCountSum;
    }

    public int GetMinAlignedRanges(bool isBullish) =>
        isBullish ? BullishMinAlignedRanges : BearishMinAlignedRanges;

    public int GetMaxAlignedRanges(bool isBullish) =>
        isBullish ? BullishMaxAlignedRanges : int.MaxValue;

    public int GetMinAlignedAnchorRanges(bool isBullish) =>
        isBullish ? BullishMinAlignedAnchorRanges : BearishMinAlignedAnchorRanges;

    public int GetMaxConflictingAnchorRanges(bool isBullish) =>
        isBullish ? BullishMaxConflictingAnchorRanges : BearishMaxConflictingAnchorRanges;

    public int GetMinTrendCountSum(bool isBullish, int? overrideMinTrendCountSum = null)
    {
        var configured = isBullish ? BullishMinTrendCountSum : BearishMinTrendCountSum;
        return overrideMinTrendCountSum.HasValue
            ? Math.Max(configured, overrideMinTrendCountSum.Value)
            : configured;
    }

    public int GetMaxTrendCountSum(bool isBullish) =>
        isBullish ? BullishMaxTrendCountSum : int.MaxValue;
}