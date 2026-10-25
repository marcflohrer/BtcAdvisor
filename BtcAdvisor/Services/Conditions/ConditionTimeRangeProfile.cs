using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Conditions;

internal static class ConditionTimeRangeProfile
{
    public static readonly IReadOnlyList<string> LegacyRanges = ["5m", "15m", "30m", "1h", "2h"];
    public static readonly IReadOnlyList<string> LongRanges = ["2h", "4h", "12h", "1d", "1w"];
    private static readonly IReadOnlyList<string> LegacyExclusiveRanges = ["5m", "15m", "30m", "1h"];
    private static readonly IReadOnlyList<string> LongExclusiveRanges = ["4h", "12h", "1d", "1w"];

    public static IReadOnlyList<string> DecisionRanges =>
        LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe ? LongRanges : LegacyRanges;

    public static IReadOnlyList<string> ResolveDecisionRanges(
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics)
    {
        var hasLongExclusive = HasAnyKey(LongExclusiveRanges, oscillatorStatistics, movingAverageStatistics);
        var hasLegacyExclusive = HasAnyKey(LegacyExclusiveRanges, oscillatorStatistics, movingAverageStatistics);

        if (hasLongExclusive && !hasLegacyExclusive)
        {
            return LongRanges;
        }

        if (hasLegacyExclusive && !hasLongExclusive)
        {
            return LegacyRanges;
        }

        return DecisionRanges;
    }

    public static string FirstRange => DecisionRanges[0];
    public static string SecondRange => DecisionRanges[1];
    public static string ThirdRange => DecisionRanges[2];
    public static string FourthRange => DecisionRanges[3];

    private static bool HasAnyKey(
        IReadOnlyList<string> ranges,
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics)
    {
        foreach (var range in ranges)
        {
            if (oscillatorStatistics.ContainsKey(range) || movingAverageStatistics.ContainsKey(range))
            {
                return true;
            }
        }

        return false;
    }
}
