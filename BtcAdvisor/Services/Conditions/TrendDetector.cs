using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Conditions;

public static class TrendDetector
{
    public static bool HasAnyTrend(BitcoinTimeRangeStatistics stat) 
        => stat.IndicatorWeakTrendCount > 0 || stat.IndicatorMediumTrendCount > 0 || stat.IndicatorStrongTrendCount > 0;

    public static bool HasMediumOrStrongTrend(BitcoinTimeRangeStatistics stat) 
        => stat.IndicatorMediumTrendCount > 0 || stat.IndicatorStrongTrendCount > 0;
    public static bool HasMediumTrend(BitcoinTimeRangeStatistics stat) 
        => stat.IndicatorMediumTrendCount > 0;

    public static bool HasStrongTrend(BitcoinTimeRangeStatistics stat) 
        => stat.IndicatorStrongTrendCount > 0;
}