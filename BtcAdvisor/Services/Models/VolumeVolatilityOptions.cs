namespace BtcAdvisor.Services.Models;

/// <summary>
/// Configuration for additive Bollinger-Band-Width and OBV-slope votes that adjust
/// the linear bullish/bearish score before regime-gate evaluation.
/// Defaults are off / weight=0 to preserve backward compatibility.
/// </summary>
public class VolumeVolatilityOptions
{
    /// <summary>When false, all score adjustments are skipped.</summary>
    public bool EnableScoreBonus { get; set; }

    /// <summary>Kline interval used to source volatility/volume context.</summary>
    public string Interval { get; set; } = "1d";

    /// <summary>Bollinger-band lookback period.</summary>
    public int BbPeriod { get; set; } = 20;

    /// <summary>Bollinger-band standard-deviation multiplier.</summary>
    public double BbStdDev { get; set; } = 2.0;

    /// <summary>Window size to compute moving average of band width (used to detect squeeze/expansion).</summary>
    public int BbWidthMaPeriod { get; set; } = 20;

    /// <summary>Window size for the OBV slope (in bars).</summary>
    public int ObvSlopeWindow { get; set; } = 10;

    /// <summary>BB width considered a squeeze when current width &lt; squeezeRatio · widthMa.</summary>
    public double BbSqueezeRatio { get; set; } = 0.7;

    /// <summary>BB width considered an expansion when current width &gt; expansionRatio · widthMa.</summary>
    public double BbExpansionRatio { get; set; } = 1.3;

    /// <summary>Bonus added to the linear bullish/bearish score on band expansion (favours dominant direction).</summary>
    public double BbExpansionVoteWeight { get; set; } = 1.0;

    /// <summary>Penalty subtracted from the linear bullish score during a squeeze (low conviction).</summary>
    public double BbSqueezeVotePenalty { get; set; } = 1.0;

    /// <summary>Bonus added to the linear bullish score when OBV slope is above this threshold (in %).</summary>
    public double ObvBullishSlopeThresholdPercent { get; set; } = 0.5;

    /// <summary>Bonus added to the linear bearish score when OBV slope is below the negative of this threshold (in %).</summary>
    public double ObvBearishSlopeThresholdPercent { get; set; } = 0.5;

    /// <summary>Weight added when the OBV slope crosses the bullish/bearish threshold.</summary>
    public double ObvVoteWeight { get; set; } = 1.5;

    /// <summary>Rolling VWAP window size (in bars).</summary>
    public int VwapPeriod { get; set; } = 20;

    /// <summary>Lookback (in bars) used to detect a "VWAP reclaim" (below within window, above now).</summary>
    public int VwapReclaimLookback { get; set; } = 5;

    /// <summary>Bonus added to the linear bullish score when a VWAP reclaim is detected.</summary>
    public double VwapReclaimVoteWeight { get; set; } = 1.5;

    /// <summary>Bonus added to bullish/bearish when price is firmly above/below VWAP by more than this %.</summary>
    public double VwapTrendDistancePercent { get; set; } = 1.0;

    /// <summary>Weight added for the VWAP-trend vote.</summary>
    public double VwapTrendVoteWeight { get; set; } = 0.5;

    /// <summary>MFI period (default 14).</summary>
    public int MfiPeriod { get; set; } = 14;

    /// <summary>MFI value below which the market is considered oversold (bullish vote).</summary>
    public double MfiOversoldThreshold { get; set; } = 20d;

    /// <summary>MFI value above which the market is considered overbought (bearish vote).</summary>
    public double MfiOverboughtThreshold { get; set; } = 80d;

    /// <summary>Weight applied when MFI crosses oversold/overbought thresholds.</summary>
    public double MfiVoteWeight { get; set; } = 1.0;
}
