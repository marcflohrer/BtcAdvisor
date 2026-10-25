using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Models.Extensions;

public static class TimeRangeStatisticsDtoExtensions
{
    private static readonly string[] MacroTimeRanges = ["2h", "4h", "12h", "1d", "1w"];

    private static (int Sell, int Buy, int Strong, int Medium, int Weak, int NoTrend) GetCounts(
        Dictionary<string, BitcoinTimeRangeStatistics> map,
        string timeRange)
    {
        if (!map.TryGetValue(timeRange, out var stat))
        {
            return (0, 0, 0, 0, 0, 0);
        }

        return (
            stat.IndicatorSellCount,
            stat.IndicatorBuyCount,
            stat.IndicatorStrongTrendCount,
            stat.IndicatorMediumTrendCount,
            stat.IndicatorWeakTrendCount,
            stat.IndicatorNoTrendCount);
    }

    public static string ToOsciTestCase (this TimeRangeStatisticsDto timeRangeStatisticsDto, string tradeAdvice)
    {
        var parts = new List<string>();
        foreach (var tf in MacroTimeRanges)
        {
            var c = GetCounts(timeRangeStatisticsDto.OscillatorStatistics, tf);
            parts.Add($"/*{tf}*/{c.Sell},{c.Buy},/*Trend*/{c.Strong},{c.Medium},{c.Weak},{c.NoTrend}");
        }

        return $"[TestCase({string.Join(',', parts)},\"{tradeAdvice}\")]";
    }

    public static string ToTestCase (this TimeRangeStatisticsDto timeRangeStatisticsDto, string tradeAdvice)
    {
        var osciParts = new List<string>();
        var maParts = new List<string>();

        foreach (var tf in MacroTimeRanges)
        {
            var o = GetCounts(timeRangeStatisticsDto.OscillatorStatistics, tf);
            osciParts.Add($"/*{tf}*/{o.Sell},{o.Buy},/*Trend*/{o.Strong},{o.Medium},{o.Weak},{o.NoTrend}");

            var m = GetCounts(timeRangeStatisticsDto.MovingAverageStatistics, tf);
            maParts.Add($"/*{tf}*/{m.Sell},{m.Buy},/*Trend*/{m.Strong},{m.Medium},{m.Weak},{m.NoTrend}");
        }

        return $"[TestCase(/*oscillator*/{string.Join(',', osciParts)},/*movingaverage*/{string.Join(',', maParts)},\"{tradeAdvice}\")]";
    }
}
