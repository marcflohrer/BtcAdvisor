using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;

public enum TurningPointKind
{
    LocalHigh,
    LocalLow
}

public sealed record TurningPoint(DateTime Timestamp, decimal ClosePrice, TurningPointKind Kind);

public static class TurningPointDetector
{
    public const int DefaultWindowDays = 30;
    public const int MinWindowDays = 7;
    public const int MaxWindowDays = 120;

    /// <summary>
    /// Detects local highs and lows using a configurable ±window price window.
    /// An additional minimum-swing filter ensures that consecutive detected
    /// turning points differ by at least <paramref name="minSwingFraction"/>.
    /// A depth filter requires each point to deviate from the local neighborhood
    /// median by at least <paramref name="minDepthFraction"/>.
    /// </summary>
    public static IReadOnlyList<TurningPoint> DetectByWindow(
        IEnumerable<StockCandle> candles,
        int windowDays = DefaultWindowDays,
        double minSwingFraction = 0.07,
        double minDepthFraction = 0.07)
    {
        if (windowDays < MinWindowDays || windowDays > MaxWindowDays)
        {
            throw new ArgumentOutOfRangeException(nameof(windowDays),
                $"windowDays must be between {MinWindowDays} and {MaxWindowDays}.");
        }

        var window = TimeSpan.FromDays(windowDays);
        var ordered = candles.OrderBy(c => c.Timestamp).ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        var result = new List<TurningPoint>();

        foreach (var candle in ordered)
        {
            var hasFullLookback = ordered.Any(x => x.Timestamp <= candle.Timestamp - window);
            var hasFullLookahead = ordered.Any(x => x.Timestamp >= candle.Timestamp + window);
            if (!hasFullLookback || !hasFullLookahead)
            {
                continue;
            }

            var before = ordered
                .Where(x => x.Timestamp < candle.Timestamp && x.Timestamp >= candle.Timestamp - window)
                .Select(x => x.ClosePrice)
                .ToList();

            var after = ordered
                .Where(x => x.Timestamp > candle.Timestamp && x.Timestamp <= candle.Timestamp + window)
                .Select(x => x.ClosePrice)
                .ToList();

            // A turning point needs context on both sides.
            if (before.Count == 0 || after.Count == 0)
            {
                continue;
            }

            var isHigh = before.All(x => candle.ClosePrice >= x) && after.All(x => candle.ClosePrice >= x);
            var isLow = before.All(x => candle.ClosePrice <= x) && after.All(x => candle.ClosePrice <= x);
            var neighborhood = before.Concat(after).ToList();
            if (neighborhood.Count == 0)
            {
                continue;
            }

            var neighborhoodMedian = MedianPrice(neighborhood);
            if (neighborhoodMedian <= 0m)
            {
                continue;
            }

            var depth = Math.Abs((double)(candle.ClosePrice - neighborhoodMedian)) / (double)neighborhoodMedian;
            var hasMinimumDepth = depth >= minDepthFraction;

            if (isHigh && !isLow && hasMinimumDepth)
            {
                result.Add(new TurningPoint(candle.Timestamp, candle.ClosePrice, TurningPointKind.LocalHigh));
            }
            else if (isLow && !isHigh && hasMinimumDepth)
            {
                result.Add(new TurningPoint(candle.Timestamp, candle.ClosePrice, TurningPointKind.LocalLow));
            }
        }

        return ApplyMinimumSwingFilter(result, minSwingFraction);
    }

    public static IReadOnlyList<TurningPoint> DetectByThirtyDayWindow(
        IEnumerable<StockCandle> candles,
        double minSwingFraction = 0.07,
        double minDepthFraction = 0.07)
    {
        return DetectByWindow(candles, DefaultWindowDays, minSwingFraction, minDepthFraction);
    }

    /// <summary>
    /// Removes turning points where the price swing to the preceding kept
    /// turning point is smaller than <paramref name="minSwingFraction"/>.
    /// When two consecutive points are too close:
    /// - Same kind  → keep the more extreme one (lower low / higher high).
    /// - Different kind → discard the second (the "reversal" is not significant).
    /// Repeats until the list is stable.
    /// </summary>
    public static List<TurningPoint> ApplyMinimumSwingFilter(
        List<TurningPoint> points,
        double minSwingFraction)
    {
        if (points.Count <= 1)
            return points;

        var output = new List<TurningPoint>(points);
        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i + 1 < output.Count; i++)
            {
                var a = output[i];
                var b = output[i + 1];
                var swing = Math.Abs((double)(b.ClosePrice - a.ClosePrice)) / (double)a.ClosePrice;

                if (swing >= minSwingFraction && a.Kind != b.Kind)
                    continue;

                if (a.Kind == b.Kind)
                {
                    // Same kind: discard the less extreme of the two.
                    var removeA = a.Kind == TurningPointKind.LocalLow
                        ? a.ClosePrice > b.ClosePrice   // a is the higher (weaker) low
                        : a.ClosePrice < b.ClosePrice;  // a is the lower (weaker) high
                    output.RemoveAt(removeA ? i : i + 1);
                }
                else
                {
                    // Different kind but swing too small: discard the second point.
                    output.RemoveAt(i + 1);
                }

                changed = true;
                break;
            }
        } while (changed);

        return output;
    }

    private static decimal MedianPrice(List<decimal> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        if (values.Count % 2 == 1)
        {
            return values[middle];
        }

        return (values[middle - 1] + values[middle]) / 2m;
    }
}
