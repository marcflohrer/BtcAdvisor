using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public static class AthDrawdownThresholdPolicy
{
    public static double ResolveOverrideThresholdPercent(BearMarketFilterOptions options, DateTime asOfUtc)
        => ResolveThresholdPercent(options, asOfUtc, options.AthDrawdownOverrideThresholdPercent);

    public static double ResolveSuppressThresholdPercent(BearMarketFilterOptions options, DateTime asOfUtc)
    {
        if (!options.AthDrawdownSuppressAdaptiveThresholdEnabled)
        {
            return options.AthDrawdownSuppressBearishSellThresholdPercent;
        }

        return ResolveThresholdPercent(options, asOfUtc, options.AthDrawdownSuppressBearishSellThresholdPercent);
    }

    public static int ResolveCycleNumber(DateTime asOfUtc, IReadOnlyList<int> cycleStartYears)
    {
        if (cycleStartYears.Count == 0)
        {
            return 1;
        }

        var year = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc).Year;
        var cycle = 1;
        for (var i = 0; i < cycleStartYears.Count; i++)
        {
            if (year >= cycleStartYears[i])
            {
                cycle = i + 1;
            }
        }

        return cycle;
    }

    private static double ResolveThresholdPercent(BearMarketFilterOptions options, DateTime asOfUtc, double fallbackThreshold)
    {
        if (!options.AthDrawdownAdaptiveThresholdEnabled)
        {
            return fallbackThreshold;
        }

        var cycle = ResolveCycleNumber(asOfUtc, options.AthDrawdownAdaptiveCycleStartYears);
        var rawThreshold = ResolveRawAdaptiveThreshold(options, cycle, fallbackThreshold);

        var min = options.AthDrawdownAdaptiveMinThresholdPercent;
        var max = options.AthDrawdownAdaptiveMaxThresholdPercent;
        if (min > max)
        {
            (min, max) = (max, min);
        }

        var result = Math.Clamp(rawThreshold, min, max);
        return result;
    }

    private static double ResolveRawAdaptiveThreshold(BearMarketFilterOptions options, int cycle, double fallbackThreshold)
    {
        if (string.Equals(options.AthDrawdownAdaptiveMode, "PreviousCycleMinusDelta", StringComparison.OrdinalIgnoreCase))
        {
            // Cycle is 1-based. For cycle 1 no previous cycle exists, so we keep fallback behavior.
            var previousCycleIndex = cycle - 2;
            if (previousCycleIndex >= 0
                && options.AthDrawdownAdaptiveCycleDrawdownPercents.Length > previousCycleIndex)
            {
                return options.AthDrawdownAdaptiveCycleDrawdownPercents[previousCycleIndex]
                       - options.AthDrawdownAdaptivePreviousCycleDeltaPercent;
            }

            return fallbackThreshold;
        }

        if (string.Equals(options.AthDrawdownAdaptiveMode, "PreviousCycleMinusMedianDelta", StringComparison.OrdinalIgnoreCase))
        {
            // Cycle is 1-based. For cycle 1 no previous cycle exists, so we keep fallback behavior.
            var previousCycleIndex = cycle - 2;
            if (previousCycleIndex >= 0
                && options.AthDrawdownAdaptiveCycleDrawdownPercents.Length > previousCycleIndex)
            {
                var previousDrawdown = options.AthDrawdownAdaptiveCycleDrawdownPercents[previousCycleIndex];
                var delta = ResolveMedianDeltaOrConfigured(options.AthDrawdownAdaptiveCycleDrawdownPercents, previousCycleIndex, options.AthDrawdownAdaptivePreviousCycleDeltaPercent);
                return previousDrawdown - delta;
            }

            return fallbackThreshold;
        }

        return options.AthDrawdownAdaptiveBasePercent - (options.AthDrawdownAdaptiveStepPercent * cycle);
    }

    private static double ResolveMedianDeltaOrConfigured(double[] drawdowns, int previousCycleIndex, double configuredDelta)
    {
        var deltas = new List<double>();
        for (var i = 1; i <= previousCycleIndex; i++)
        {
            deltas.Add(drawdowns[i - 1] - drawdowns[i]);
        }

        if (deltas.Count == 0)
        {
            return configuredDelta;
        }

        deltas.Sort();
        var middle = deltas.Count / 2;
        return deltas.Count % 2 == 1
            ? deltas[middle]
            : (deltas[middle - 1] + deltas[middle]) / 2.0;
    }
}
