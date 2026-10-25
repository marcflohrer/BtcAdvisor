using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Models;

public record TimeRangeStatisticsDto(Dictionary<string, BitcoinTimeRangeStatistics> OscillatorStatistics, Dictionary<string, BitcoinTimeRangeStatistics> MovingAverageStatistics);
