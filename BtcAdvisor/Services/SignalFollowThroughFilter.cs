namespace BtcAdvisor.Services;

public readonly record struct SignalFollowThroughPoint(DateTime Timestamp, decimal Price);

public static class SignalFollowThroughFilter
{
    public static bool PassesBullish(decimal currentPrice, decimal? previousAcceptedPrice, double minRisePercent)
    {
        return PassesDirectional(currentPrice, previousAcceptedPrice, minRisePercent, isBullish: true);
    }

    public static bool PassesBearish(decimal currentPrice, decimal? previousAcceptedPrice, double minDropPercent)
    {
        return PassesDirectional(currentPrice, previousAcceptedPrice, minDropPercent, isBullish: false);
    }

    public static IReadOnlyList<SignalFollowThroughPoint> FilterOrdered(
        IEnumerable<SignalFollowThroughPoint> orderedPoints,
        bool isBullish,
        double minMovePercent)
    {
        var filtered = new List<SignalFollowThroughPoint>();
        decimal? previousAcceptedPrice = null;

        foreach (var point in orderedPoints.OrderBy(x => x.Timestamp))
        {
            if (!PassesDirectional(point.Price, previousAcceptedPrice, minMovePercent, isBullish))
            {
                continue;
            }

            filtered.Add(point);
            previousAcceptedPrice = point.Price;
        }

        return filtered;
    }

    public static double ComputeDirectionalMovePercent(decimal previousPrice, decimal currentPrice, bool isBullish)
    {
        if (previousPrice <= 0)
        {
            return double.PositiveInfinity;
        }

        var rawPercent = (double)((currentPrice - previousPrice) / previousPrice * 100m);
        return isBullish ? rawPercent : -rawPercent;
    }

    private static bool PassesDirectional(decimal currentPrice, decimal? previousAcceptedPrice, double minMovePercent, bool isBullish)
    {
        if (!previousAcceptedPrice.HasValue || previousAcceptedPrice <= 0)
        {
            return true;
        }

        return ComputeDirectionalMovePercent(previousAcceptedPrice.Value, currentPrice, isBullish) >= minMovePercent;
    }
}