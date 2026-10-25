using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public static class MicroStrategyFairValueCalculator
{
    public static MicroStrategyFairValue GetFairValue(double BitcoinValue)
    {
        var averageValue = (0.0031 * BitcoinValue) - 45.732;
        return new MicroStrategyFairValue(averageValue - 25, averageValue + 26);
    }
}
