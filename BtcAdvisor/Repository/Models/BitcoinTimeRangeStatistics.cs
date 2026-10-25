namespace BtcAdvisor.Repository.Models;

public class BitcoinTimeRangeStatistics
{
    public double TimespanHours { get; set; }
    public int IndicatorSellCount { get; set; }
    public int IndicatorNeutralCount { get; set; }
    public int IndicatorBuyCount { get; set; }
    public int IndicatorStrongTrendCount { get; set; }
    public int IndicatorMediumTrendCount { get; set; }
    public int IndicatorWeakTrendCount { get; set; }
    public int IndicatorNoTrendCount { get; set; }
    public int IndicatorNotFound { get; set; }
    public bool IsOscillatorIndicatorGroup { get; set; }
}
