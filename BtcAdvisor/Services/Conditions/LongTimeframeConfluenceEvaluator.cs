using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Conditions;

public static class LongTimeframeConfluenceEvaluator
{
    private static readonly IReadOnlySet<string> AnchorRanges = new HashSet<string>(StringComparer.Ordinal)
    {
        "1d",
        "1w",
    };

    public static bool IsBullish(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        LongTimeframeConfluenceThresholdSet? thresholdSet = null)
    {
        return EvaluateDirectionalConfluence(
            oscillatorStatistics,
            movingAverageStatistics,
            isBullish: true,
            thresholdSet ?? LongTimeframeConfluenceThresholdSet.Default);
    }

    public static bool IsBearish(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        LongTimeframeConfluenceThresholdSet? thresholdSet = null)
    {
        return EvaluateDirectionalConfluence(
            oscillatorStatistics,
            movingAverageStatistics,
            isBullish: false,
            thresholdSet ?? LongTimeframeConfluenceThresholdSet.Default);
    }

    public static bool HasRequiredLongTimeframeData(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics)
    {
        return ConditionTimeRangeProfile.LongRanges.All(range =>
            oscillatorStatistics.ContainsKey(range) || movingAverageStatistics.ContainsKey(range));
    }

    private static bool EvaluateDirectionalConfluence(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        bool isBullish,
        LongTimeframeConfluenceThresholdSet thresholdSet,
        int? minTrendCountOverride = null)
    {
        if (!HasRequiredLongTimeframeData(oscillatorStatistics, movingAverageStatistics))
        {
            return false;
        }

        var alignedRanges = 0;
        var alignedAnchorRanges = 0;
        var conflictingAnchorRanges = 0;

        foreach (var timeRange in ConditionTimeRangeProfile.LongRanges)
        {
            if (!oscillatorStatistics.TryGetValue(timeRange, out var oscillator))
            {
                continue;
            }

            var isAligned = IsAligned(oscillator, isBullish);
            var isConflicting = IsAligned(oscillator, !isBullish);

            if (isAligned)
            {
                alignedRanges++;
                if (AnchorRanges.Contains(timeRange))
                {
                    alignedAnchorRanges++;
                }
            }

            if (isConflicting && AnchorRanges.Contains(timeRange))
            {
                conflictingAnchorRanges++;
            }
        }

        var minAligned = thresholdSet.GetMinAlignedRanges(isBullish);
        var maxAligned = thresholdSet.GetMaxAlignedRanges(isBullish);
        var minAnchor = thresholdSet.GetMinAlignedAnchorRanges(isBullish);
        var maxConflicting = thresholdSet.GetMaxConflictingAnchorRanges(isBullish);
        var minTrend = thresholdSet.GetMinTrendCountSum(isBullish, minTrendCountOverride);

        if (!(alignedRanges >= minAligned
            && alignedRanges <= maxAligned
            && alignedAnchorRanges >= minAnchor
            && conflictingAnchorRanges <= maxConflicting))
        {
            return false;
        }

        var trendSum = ComputeTrendCountSum(oscillatorStatistics);
        var maxTrend = thresholdSet.GetMaxTrendCountSum(isBullish);
        return (minTrend == 0 || trendSum >= minTrend) && trendSum <= maxTrend;
    }

    /// <summary>
    /// Returns the total number of strong + medium trend indicators across all long timeframe ranges.
    /// Can be used to distinguish "many trend indicators aligned" days from weak confluence days.
    /// </summary>
    public static int ComputeTrendCountSum(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics)
    {
        return ConditionTimeRangeProfile.LongRanges
            .Where(oscillatorStatistics.ContainsKey)
            .Sum(range => oscillatorStatistics[range].IndicatorStrongTrendCount
                        + oscillatorStatistics[range].IndicatorMediumTrendCount);
    }

    /// <summary>
    /// Returns the number of LTF oscillator ranges that are bullish-aligned (buy count &gt; sell count).
    /// Useful as a raw BIS (Bitcoin Indicator Statistics) score at signal time.
    /// </summary>
    public static int ComputeBullishAlignedRanges(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics)
    {
        return ConditionTimeRangeProfile.LongRanges
            .Where(oscillatorStatistics.ContainsKey)
            .Count(range => IsAligned(oscillatorStatistics[range], isBullish: true));
    }

    public static double ComputeLinearBullishScore(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics)
    {
        var (buy, sell, strong, medium) = ComputeLinearSums(oscillatorStatistics);
        return (2.0 * buy) - (1.5 * sell) + (1.5 * strong) + (0.5 * medium);
    }

    public static double ComputeLinearBearishScore(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics)
    {
        var (buy, sell, strong, medium) = ComputeLinearSums(oscillatorStatistics);
        return (2.0 * sell) - (1.5 * buy) + (1.5 * strong) + (0.5 * medium);
    }

    public static bool IsBullishLinear(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        double threshold)
    {
        if (!HasRequiredLongTimeframeData(oscillatorStatistics, movingAverageStatistics))
        {
            return false;
        }

        return ComputeLinearBullishScore(oscillatorStatistics) > threshold;
    }

    public static bool IsBearishLinear(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        double threshold)
    {
        if (!HasRequiredLongTimeframeData(oscillatorStatistics, movingAverageStatistics))
        {
            return false;
        }

        return ComputeLinearBearishScore(oscillatorStatistics) > threshold;
    }

    /// <summary>
    /// Like <see cref="IsBullish"/>, but additionally requires the total trend indicator count
    /// across all long timeframe ranges to be at least <paramref name="minTrendCountSum"/>.
    /// Use this when you want confluence that is supported by multiple strong trend indicators,
    /// not just a bare oscillator/MA majority.
    /// </summary>
    public static bool IsBullishWithMinTrend(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        int minTrendCountSum,
        LongTimeframeConfluenceThresholdSet? thresholdSet = null)
    {
        return EvaluateDirectionalConfluence(
            oscillatorStatistics,
            movingAverageStatistics,
            isBullish: true,
            thresholdSet ?? LongTimeframeConfluenceThresholdSet.Default,
            minTrendCountOverride: minTrendCountSum);
    }

    /// <summary>
    /// Like <see cref="IsBearish"/>, but additionally requires the total trend indicator count
    /// across all long timeframe ranges to be at least <paramref name="minTrendCountSum"/>.
    /// </summary>
    public static bool IsBearishWithMinTrend(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        int minTrendCountSum,
        LongTimeframeConfluenceThresholdSet? thresholdSet = null)
    {
        return EvaluateDirectionalConfluence(
            oscillatorStatistics,
            movingAverageStatistics,
            isBullish: false,
            thresholdSet ?? LongTimeframeConfluenceThresholdSet.Default,
            minTrendCountOverride: minTrendCountSum);
    }

    /// <summary>
    /// Check alignment based solely on oscillator statistics, not moving averages.
    /// Alignment requires the oscillator direction to match the desired direction
    /// (buy > sell for bullish, sell > buy for bearish). Per-range trend is not
    /// required here; the global trend gate is enforced via the configured threshold set.
    /// </summary>
    private static bool IsAligned(
        BitcoinTimeRangeStatistics oscillator,
        bool isBullish)
    {
        return isBullish
            ? oscillator.IndicatorBuyCount > oscillator.IndicatorSellCount
            : oscillator.IndicatorSellCount > oscillator.IndicatorBuyCount;
    }

    private static (int buy, int sell, int strong, int medium) ComputeLinearSums(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics)
    {
        var buy = 0;
        var sell = 0;
        var strong = 0;
        var medium = 0;

        foreach (var range in ConditionTimeRangeProfile.LongRanges)
        {
            if (!oscillatorStatistics.TryGetValue(range, out var stat))
            {
                continue;
            }

            buy += stat.IndicatorBuyCount;
            sell += stat.IndicatorSellCount;
            strong += stat.IndicatorStrongTrendCount;
            medium += stat.IndicatorMediumTrendCount;
        }

        return (buy, sell, strong, medium);
    }
}