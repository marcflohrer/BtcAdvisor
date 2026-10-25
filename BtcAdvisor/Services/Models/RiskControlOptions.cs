namespace BtcAdvisor.Services.Models;

public class RiskControlOptions
{
    public bool EnableHardStopLoss { get; set; }
    public double HardStopLossPercent { get; set; } = -8.0;

    /// <summary>
    /// When true, an additional volatility-aware stop is evaluated on every tick.
    /// Effective stop = whichever of (HardStopLoss, AtrStop) is hit first (engerer gilt).
    /// </summary>
    public bool EnableAtrStopLoss { get; set; }

    /// <summary>Lookback period for the ATR computation (Wilder smoothing).</summary>
    public int AtrPeriod { get; set; } = 14;

    /// <summary>Multiplier k in: stop = entryPrice − k · ATR. Typical range 1.5–3.0.</summary>
    public double AtrMultiplier { get; set; } = 2.0;

    /// <summary>Kline interval used to source the ATR (e.g. "1d", "4h", "1h").</summary>
    public string AtrInterval { get; set; } = "1d";

    /// <summary>
    /// Slippage in percent applied to the modeled stop fill price (e.g. 0.5 → fill 0.5% worse than threshold).
    /// Used by both backtest and live to keep recorded stop-exit prices consistent and realistic.
    /// </summary>
    public double StopLossSlippagePercent { get; set; } = 0.5;

    /// <summary>
    /// Minimum hours that must elapse after a sell (or stop-loss exit) before a new buy is allowed.
    /// 0 = disabled. E.g. 168 = 7-day cooldown.
    /// </summary>
    public int ReEntryCooldownHours { get; set; } = 0;

    /// <summary>
    /// When > 0, regular Sell/DistributionSell signals are suppressed unless BTC has been below
    /// the 200-day SMA for this many consecutive calendar days. HardStopLoss and FireSell are
    /// never suppressed. 0 = disabled (default behaviour).
    /// </summary>
    public int SmaExitDebounceDays { get; set; } = 0;

    /// <summary>
    /// When true, a trailing stop is tracked for each open position. The stop only activates
    /// once the position gain from entry exceeds <see cref="TrailingStopActivationPercent"/>.
    /// Once active, a sell is triggered when the price drops more than
    /// <see cref="TrailingStopPercent"/> below the intra-trade high-water mark.
    /// </summary>
    public bool EnableTrailingStop { get; set; } = false;

    /// <summary>Minimum profit from entry (in %) required before the trailing stop activates. Default: 100.</summary>
    public double TrailingStopActivationPercent { get; set; } = 100.0;

    /// <summary>Maximum drawdown from the high-water mark (in %) that triggers the trailing stop. Default: 35.</summary>
    public double TrailingStopPercent { get; set; } = 35.0;

    /// <summary>
    /// Close-price gain from entry (in %) required to activate the breakeven stop.
    /// Once any daily close has reached this gain, the stop fires when the close price
    /// returns to or below the entry price. 0 = disabled (default). Example: 15.0.
    /// Consistent with backtest: activation uses close price, not intraday high.
    /// </summary>
    public double BreakevenTriggerPercent { get; set; } = 0.0;

    /// <summary>
    /// Minimum hours a position must be held before a regular Sell or DistributionSell signal
    /// can execute. FireSell, HardStopLoss, TrailingStop, and BreakevenStop are not affected.
    /// 0 = disabled (default). Example: 480 = 20 days.
    /// </summary>
    public int MinHoldHours { get; set; } = 0;
}
