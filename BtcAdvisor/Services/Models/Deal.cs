namespace BtcAdvisor.Services.Models;

public class Deal
{
    public string TradeAdvice { get; set; } = string.Empty;

    public DecisionSource DecisionSource { get; set; } = DecisionSource.Unknown;

    public decimal Price { get; set; }

    public double MicroStrategyLowerThreshold { get; set; }
    public double MicroStrategyUpperThreshold { get; set; }

    public DateTime TradingDate { get; set; }

    public bool isSellDeal => TradeAdvice.Contains("Sell");

    public string DebugString { get; set; } = string.Empty;

    /// <summary>BIS snapshot: bullish-aligned oscillator ranges at signal time.</summary>
    public int AlignedRanges { get; set; }

    /// <summary>BIS snapshot: strong + medium trend indicator sum at signal time.</summary>
    public int TrendCountSum { get; set; }

    public double LinearBullishScore { get; set; }
}
