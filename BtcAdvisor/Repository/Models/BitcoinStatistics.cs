namespace BtcAdvisor.Repository.Models;

public class BitcoinStatistics
{
    public double Price { get; set; }

    public IReadOnlyCollection<BitcoinTimeRangeStatistics> TimeRangeStatistics { get; set; } = [];

    public string DebugString { get; set; } = string.Empty;

    public DateTime DateTime { get; set; }
}
