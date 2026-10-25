using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Models;

public class StatsAndPrice
{
    public double Price { get; set; }
    public Dictionary<string, BitcoinTimeRangeStatistics> TimeRangeStatisticsMap { get; set; } = [];

    public string DebugString { get; set; } = string.Empty;
    public DateTime MinDateTime { get; set; } = DateTime.UtcNow;
    public DateTime MaxDateTime { get; set; } = DateTime.UtcNow;
    public List<string> Labels { get; set; } = Enumerable.Empty<string>().ToList();
}