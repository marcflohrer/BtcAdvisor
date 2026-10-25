namespace BtcAdvisor.Services.Models;

public class BearMarketFilterOptions
{
    /// <summary>When true, bullish entries are blocked while the asset is in a long-term bear regime.</summary>
    public bool Enabled { get; set; }

    /// <summary>SMA lookback period.</summary>
    public int MovingAveragePeriod { get; set; } = 200;

    /// <summary>Kline interval used to source the SMA (e.g. "1d").</summary>
    public string Interval { get; set; } = "1d";

    /// <summary>
    /// When true, the bear-market block is bypassed if the asset is at a deep-cycle low
    /// (drawdown from ATH exceeds <see cref="AthDrawdownOverrideThresholdPercent"/>).
    /// Rationale: after a sustained 75 %+ decline the remaining downside is limited and
    /// the cycle-reversal upside is asymmetric; blocking entry at that point is
    /// counter-productive.
    /// </summary>
    public bool AthDrawdownOverrideEnabled { get; set; }

    /// <summary>
    /// Minimum drawdown from ATH (as a positive percentage, e.g. 75.0 means 75 %)
    /// required for the ATH-override to apply.
    /// </summary>
    public double AthDrawdownOverrideThresholdPercent { get; set; } = 75.0;

    /// <summary>
    /// Kline interval used for ATH drawdown detection (independent from bear-market SMA interval).
    /// Defaults to 1h so deep-cycle-low detection can react intraday.
    /// </summary>
    public string AthDrawdownInterval { get; set; } = "1h";

    /// <summary>
    /// When true, regular bearish Sell / DistributionSell signals are suppressed while the
    /// asset is in a deep-cycle-low zone (drawdown from ATH ≥
    /// <see cref="AthDrawdownSuppressBearishSellThresholdPercent"/>).
    /// Emergency exits (FireSell, HardStopLoss, TrailingStop) are never suppressed.
    /// Rationale: in the deep-cycle-low zone the position should be held ("ausgesessen");
    /// premature bearish exits realise a large paper loss and miss the subsequent recovery.
    /// </summary>
    public bool AthDrawdownSuppressBearishSellEnabled { get; set; }

    /// <summary>
    /// Minimum drawdown from ATH (positive percentage) required for bearish-sell suppression
    /// to apply. Defaults to the same value as <see cref="AthDrawdownOverrideThresholdPercent"/>.
    /// </summary>
    public double AthDrawdownSuppressBearishSellThresholdPercent { get; set; } = 75.0;

    /// <summary>
    /// When true, the sell-suppression threshold uses the same adaptive formula as the buy-override
    /// threshold (<see cref="AthDrawdownAdaptiveThresholdEnabled"/>).
    /// When false (default), <see cref="AthDrawdownSuppressBearishSellThresholdPercent"/> is used
    /// as a fixed static threshold — allowing the suppress level to be tuned independently
    /// of the buy-override cycle formula.
    /// </summary>
    public bool AthDrawdownSuppressAdaptiveThresholdEnabled { get; set; }

    /// <summary>
    /// Kline interval used for ATH drawdown detection during sell suppression (independent from buy-override interval).
    /// Defaults to 1d for stable suppression zone detection (resists intraday pumps).
    /// </summary>
    public string AthDrawdownSuppressInterval { get; set; } = "1d";

    /// <summary>
    /// Enables cycle-top overvaluation sell boost. When enabled and an open position exists,
    /// Wait/non-sell signals can be upgraded to DistributionSell inside the overvaluation zone.
    /// </summary>
    public bool CycleTopSellZoneEnabled { get; set; }

    /// <summary>
    /// Kline interval used for cycle-top overvaluation zone detection.
    /// </summary>
    public string CycleTopSellZoneInterval { get; set; } = "1d";

    /// <summary>
    /// Rolling lookback window used to detect cycle low and ATH for overvaluation checks.
    /// </summary>
    public int CycleTopSellZoneLookbackDays { get; set; } = 1460;

    /// <summary>
    /// Minimum required drawup from cycle low (percent) before top-zone boost can trigger.
    /// Example: 350 means latest high must be at least +350 % above cycle low.
    /// </summary>
    public double CycleTopSellZoneMinDrawupPercent { get; set; } = 350.0;

    /// <summary>
    /// Minimum proximity of latest high to cycle ATH in percent.
    /// Example: 90 means latest high must be at least 90 % of cycle ATH.
    /// </summary>
    public double CycleTopSellZoneMinAthProximityPercent { get; set; } = 90.0;

    /// <summary>
    /// Lower bound (in months) of the post-halving window in which a cycle-top
    /// DistributionSell may fire. The zone is suppressed when the most recent
    /// halving was less than this many months ago. Historical Bitcoin cycle
    /// tops occurred 12–18 months after each halving. Set &lt;= 0 to disable
    /// the lower bound (combined with MaxMonthsAfterHalving &lt;= 0 the gate
    /// is fully disabled).
    /// </summary>
    public int CycleTopSellZoneMinMonthsAfterHalving { get; set; } = 12;

    /// <summary>
    /// Upper bound (in months) of the post-halving window in which a cycle-top
    /// DistributionSell may fire. The zone is suppressed when the most recent
    /// halving was more than this many months ago, preventing late-cycle
    /// false positives ahead of the next halving. Set &lt;= 0 to disable the
    /// upper bound. Default 30 covers historical cycle tops (12–18 months)
    /// with generous buffer for cycle extensions.
    /// </summary>
    public int CycleTopSellZoneMaxMonthsAfterHalving { get; set; } = 30;

    /// <summary>
    /// Number of days the raw cycle-top zone must remain armed before a
    /// DistributionSell may fire. The confirmation requires that the raw zone
    /// was also active <c>N</c> days earlier AND that the cycle ATH has not
    /// grown by more than <see cref="CycleTopSellZoneConfirmationAthTolerancePercent"/>
    /// in the meantime — i.e. the rally has stalled near the ATH.
    /// Set &lt;= 0 to disable confirmation (raw signal fires immediately).
    /// Class default is 0 (off) so existing tests keep their behaviour; production
    /// opts in via appsettings.
    /// </summary>
    public int CycleTopSellZoneConfirmationDays { get; set; }

    /// <summary>
    /// Maximum allowed growth of the cycle ATH (in percent) between the armed
    /// timestamp (asOf − <see cref="CycleTopSellZoneConfirmationDays"/>) and the
    /// evaluation timestamp. If the cycle ATH grew by more than this fraction,
    /// the rally is still extending and confirmation is denied. Default 1.0 %
    /// tolerates minor noise without accepting a meaningful breakout.
    /// </summary>
    public double CycleTopSellZoneConfirmationAthTolerancePercent { get; set; } = 1.0;

    /// <summary>
    /// Enables adaptive deep-cycle-low threshold calculation by cycle:
    /// - FormulaByCycle: threshold = AthDrawdownAdaptiveBasePercent - AthDrawdownAdaptiveStepPercent * cycleNumber.
    /// - PreviousCycleMinusDelta: threshold = previousCycleDrawdown - AthDrawdownAdaptivePreviousCycleDeltaPercent.
    /// - PreviousCycleMinusMedianDelta: threshold = previousCycleDrawdown - median(previousCycleDeltas).
    /// </summary>
    public bool AthDrawdownAdaptiveThresholdEnabled { get; set; }

    /// <summary>
    /// Adaptive mode selector. Supported values:
    /// - FormulaByCycle (default)
    /// - PreviousCycleMinusDelta
    /// - PreviousCycleMinusMedianDelta
    /// Unknown values fall back to FormulaByCycle.
    /// </summary>
    public string AthDrawdownAdaptiveMode { get; set; } = "FormulaByCycle";

    /// <summary>
    /// Base percentage for adaptive threshold formula. Default 100.
    /// </summary>
    public double AthDrawdownAdaptiveBasePercent { get; set; } = 100.0;

    /// <summary>
    /// Per-cycle decrement for adaptive threshold formula. Default 7.
    /// </summary>
    public double AthDrawdownAdaptiveStepPercent { get; set; } = 7.0;

    /// <summary>
    /// Drawdown history in percent per cycle (absolute values, e.g. 93 means -93 %),
    /// aligned with <see cref="AthDrawdownAdaptiveCycleStartYears"/>.
    /// Used by mode PreviousCycleMinusDelta.
    /// Example: [93, 87, 84, 77, 70].
    /// </summary>
    public double[] AthDrawdownAdaptiveCycleDrawdownPercents { get; set; } = [];

    /// <summary>
    /// Delta in percentage points subtracted from previous cycle drawdown in mode PreviousCycleMinusDelta.
    /// Example default: 7.
    /// </summary>
    public double AthDrawdownAdaptivePreviousCycleDeltaPercent { get; set; } = 7.0;

    /// <summary>
    /// Cycle start years used to resolve cycleNumber from asOfUtc. Default: 2011, 2013, 2017, 2021, 2026.
    /// </summary>
    public int[] AthDrawdownAdaptiveCycleStartYears { get; set; } = [];

    /// <summary>
    /// When true, a Pi Cycle Top signal (111DMA ≥ 350DMA×2 on daily candles) upgrades
    /// non-sell advice to DistributionSell while a position is open.
    /// Historically this crossover occurred within days of the 2013, 2017, and 2021 cycle peaks.
    /// </summary>
    public bool PiCycleTopSellEnabled { get; set; }

    /// <summary>
    /// Kline interval used for Pi Cycle Top SMA computation. Must be "1d" for the indicator
    /// to have its classic definition (daily SMA(111) vs daily SMA(350)×2).
    /// </summary>
    public string PiCycleTopInterval { get; set; } = "1d";

    /// <summary>
    /// Lower clamp for adaptive threshold to keep values within valid deep-cycle range.
    /// </summary>
    public double AthDrawdownAdaptiveMinThresholdPercent { get; set; } = 1.0;

    /// <summary>
    /// Upper clamp for adaptive threshold to keep values below 100 %.
    /// </summary>
    public double AthDrawdownAdaptiveMaxThresholdPercent { get; set; } = 99.0;

    /// <summary>
    /// When true, the bear-market buy block is bypassed when the approximate Puell Multiple
    /// (daily close / 365-day SMA of closes) is below <see cref="PuellMultipleBuyBoostThreshold"/>.
    /// Historically Puell &lt; 0.5 marks deep-cycle lows with asymmetric upside.
    /// </summary>
    public bool PuellMultipleBuyBoostEnabled { get; set; }

    /// <summary>
    /// Puell Multiple threshold (ratio of close to SMA(365)) below which the buy boost applies.
    /// Default 0.5 (price 50 % below the 365-day average, a historically reliable accumulation zone).
    /// </summary>
    public double PuellMultipleBuyBoostThreshold { get; set; } = 0.5;

    /// <summary>
    /// Kline interval used for Puell Multiple SMA computation. Should be "1d".
    /// </summary>
    public string PuellMultipleInterval { get; set; } = "1d";

    /// <summary>
    /// When true, new buys are blocked when the current close price is above
    /// <see cref="AthProximityBuyBlockMaxPercent"/> percent of the running all-time high.
    /// Rationale: buying near the ATH has historically produced negative returns in BTC cycles.
    /// Emergency overrides (PuellMultiple, AthDrawdownOverride) are applied before this gate
    /// and can still allow a buy in deep-cycle-low conditions.
    /// </summary>
    public bool AthProximityBuyBlockEnabled { get; set; }

    /// <summary>
    /// Maximum allowed price proximity to ATH (as a percentage of ATH, e.g. 85.0 means
    /// "block if current close > 85 % of all-time high"). Must be in range (0, 100].
    /// </summary>
    public double AthProximityBuyBlockMaxPercent { get; set; } = 85.0;

    /// <summary>
    /// Kline interval used to source ATH and current close for proximity check. Should be "1d".
    /// </summary>
    public string AthProximityBuyBlockInterval { get; set; } = "1d";

    /// <summary>
    /// Maximum age of the running ATH (in days) for the proximity gate to be active.
    /// If the ATH was set more than this many days ago, the proximity check is skipped —
    /// allowing accumulation entries before a new cycle breakout.
    /// Set to 0 to disable the age constraint (gate always active). Default: 180 (6 months).
    /// </summary>
    public int AthProximityBuyBlockMaxAthAgeDays { get; set; } = 180;

    /// <summary>
    /// When true, regular Sell and DistributionSell signals are suppressed while the current
    /// price is above <see cref="NearAthSellSuppressMinPercent"/> percent of the running ATH.
    /// Rationale: selling near the ATH during an ongoing bull run locks in too early; the
    /// remaining upside tends to be large and the position should be held until a meaningful
    /// reversal signal (FireSell, HardStopLoss, TrailingStop) fires instead.
    /// FireSell, HardStopLoss, TrailingStop, and BreakevenStop bypass this gate.
    /// </summary>
    public bool NearAthSellSuppressEnabled { get; set; }

    /// <summary>
    /// Minimum ATH proximity percentage (e.g. 70.0 means "suppress sells when price ≥ 70 % of ATH").
    /// Only applies when <see cref="NearAthSellSuppressEnabled"/> is true.
    /// </summary>
    public double NearAthSellSuppressMinPercent { get; set; } = 65.0;

    /// <summary>
    /// Kline interval used to compute the running ATH for sell suppression.
    /// Should match <see cref="AthProximityBuyBlockInterval"/> so both gates use the same ATH baseline.
    /// Defaults to "1d".
    /// </summary>
    public string NearAthSellSuppressInterval { get; set; } = "1d";

    /// <summary>
    /// When true, new buy signals are suppressed for <see cref="PostCycleTopBuyBlockDays"/> days
    /// after a cycle-top forced sell (Pi Cycle Top or CycleTopZone).
    /// Prevents whipsaw re-entries immediately after a macro top exit.
    /// </summary>
    public bool PostCycleTopBuyBlockEnabled { get; set; }

    /// <summary>
    /// Number of days to suppress new buys after a cycle-top forced sell.
    /// Only applies when <see cref="PostCycleTopBuyBlockEnabled"/> is true.
    /// Defaults to 90.
    /// </summary>
    public int PostCycleTopBuyBlockDays { get; set; } = 90;

    /// <summary>
    /// When true, new buy entries after a DistributionSell are additionally blocked while
    /// the current price exceeds <see cref="PostCycleTopSmaRatioMaxMultiplier"/> × SMA(<see cref="PostCycleTopSmaRatioPeriodDays"/>).
    /// Inspired by the 2-Year MA Multiplier: a ratio &gt; 1.5 historically indicates macro
    /// overvaluation and a structurally poor re-entry point.
    /// The gate stays active until the price falls below the multiplier threshold — no day-count expiry.
    /// Requires <see cref="PostCycleTopBuyBlockEnabled"/> to be true (shares the same lastCycleTopForcedSellAt tracker).
    /// </summary>
    public bool PostCycleTopSmaRatioBlockEnabled { get; set; }

    /// <summary>
    /// SMA period in days for the ratio gate. Default 730 (≈ 2-Year MA).
    /// </summary>
    public int PostCycleTopSmaRatioPeriodDays { get; set; } = 730;

    /// <summary>
    /// Maximum allowed price/SMA ratio for re-entry after a DistributionSell.
    /// Example: 1.5 means block buy while price &gt; 1.5 × SMA(730d).
    /// Default 1.5.
    /// </summary>
    public double PostCycleTopSmaRatioMaxMultiplier { get; set; } = 1.5;

    /// <summary>
    /// When true, new buy entries after a DistributionSell are additionally blocked while
    /// the current price is still above <see cref="PostCycleTopAthDrawdownMaxAthProximityPercent"/> %
    /// of the running all-time high. Prevents re-entries in the 40–65 % ATH zone of bear markets
    /// (e.g. Jan 2022 @ 62 % of $69k ATH) while allowing deep-cycle-low entries
    /// (e.g. Mar 2018 @ 39 % of $20k ATH).
    /// No time-expiry — clears automatically once price drops far enough.
    /// Requires <see cref="PostCycleTopBuyBlockEnabled"/> to be true.
    /// </summary>
    public bool PostCycleTopAthDrawdownBlockEnabled { get; set; }

    /// <summary>
    /// Maximum allowed price proximity to ATH (as positive percentage, e.g. 50.0 means
    /// "block while price > 50 % of running ATH"). Default 50.0.
    /// </summary>
    public double PostCycleTopAthDrawdownMaxAthProximityPercent { get; set; } = 50.0;

    /// <summary>
    /// When true, the realtime trade pipeline operates in macro-only mode: the full
    /// micro-signal advisor is ignored and trade advice is derived purely from macro maps
    /// (CycleTopZone → DistributionSell, DeepCycleLow → StrongBuy, ATH-Breakout → AthBreakoutBuy).
    /// This mirrors the backtested +2,831 % strategy (Phase 5b, ADR-017).
    /// </summary>
    public bool MacroOnlyMode { get; set; }
}
