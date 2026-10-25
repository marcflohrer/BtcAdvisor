using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;

public interface IBitcoinStatisticsService
{
    public Task<BitcoinStatistics> GetStatistics (DateTime dateTime, bool IsOszillator, string timeSpan, CancellationToken cancellationToken);
}
