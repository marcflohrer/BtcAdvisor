namespace BtcAdvisor.Services.Models;

public class BitcoinTradeStatus
{
    public string TradeAdvice { get; set;} = "None";

    public DecisionSource DecisionSource { get; set; } = DecisionSource.Unknown;

    public double CurrentPrice { get; set; }

    public double MicroStrategyLowerThreshold { get; set; } = 0;
    public double MicroStrategyUpperThreshold { get; set; } = 0;

    public string DebugString { get; set; } = string.Empty;
    public DateTime MinDateTime { get; set; } = DateTime.UtcNow;
    public DateTime MaxDateTime { get; set; } = DateTime.UtcNow;

    /// <summary>Number of LTF oscillator ranges that are bullish-aligned at signal time.</summary>
    public int AlignedRanges { get; set; }

    /// <summary>Sum of strong + medium trend indicators across all LTF ranges at signal time.</summary>
    public int TrendCountSum { get; set; }

    public double LinearBullishScore { get; set; }

    public double LinearBearishScore { get; set; }
}
