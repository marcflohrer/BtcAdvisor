using BtcAdvisor.Services.Conditions;

namespace BtcAdvisor.Services.Models;

/// <summary>Ein Ergebnis-Zeile aus dem Threshold-Grid-Sweep.</summary>
public record BacktestSweepRow(
    double MomentumBrakeMaxRatio,
    double TroughMinDrawdownPercent,
    double TroughRecoveryProximityPercent,
    double BearishRegimeMinLinearBullishScore,
    double? TotalProfitPercent,
    double? BuyAndHoldPercent,
    double? Delta,
    int TotalBuys,
    int TotalSells,
    double? WinRatePercent,
    double? MaxLossPercent,
    double? MaxGainPercent,
    int BullishMinAlignedRanges = LongTimeframeThresholds.BullishMinAlignedRanges,
    int BullishMaxAlignedRanges = int.MaxValue,
    int BullishMinAlignedAnchorRanges = LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
    int BullishMinTrendCountSum = LongTimeframeThresholds.BullishMinTrendCountSum,
    int BullishMaxTrendCountSum = int.MaxValue,
    int BearishMinAlignedRanges = LongTimeframeThresholds.BearishMinAlignedRanges,
    int BearishMinTrendCountSum = LongTimeframeThresholds.BearishMinTrendCountSum,
    bool BullishRegimeBlockBuy = false,
    bool EntryRequiresPriceAboveSma200 = false,
    double BreakevenTriggerPercent = 0.0
);

public class BacktestTrade
{
    public DateTime Timestamp { get; set; }
    public string TradeAdvice { get; set; } = string.Empty;
    public decimal Price { get; set; }
    /// <summary>Prozentualer Gewinn/Verlust bezogen auf den letzten Kauf (nur bei Sell-Signalen).</summary>
    public double? ProfitPercent { get; set; }

    // ── Postmortem fields (only set on Sell/Stop trades) ─────────────────────
    /// <summary>Entry timestamp of the closed position (only on Sell/Stop trades).</summary>
    public DateTime? EntryTimestamp { get; set; }
    /// <summary>Entry price of the closed position (only on Sell/Stop trades).</summary>
    public decimal? EntryPrice { get; set; }
    /// <summary>Holding duration in hours from entry to exit.</summary>
    public double? HoldHours { get; set; }
    /// <summary>
    /// Maximum Favorable Excursion: highest unrealized profit % between entry and exit.
    /// Indicates whether winners ran (high MFE-vs-realized-profit gap = exited too early).
    /// </summary>
    public double? MaxFavorableExcursionPercent { get; set; }
    /// <summary>
    /// Maximum Adverse Excursion: deepest unrealized loss % between entry and exit.
    /// Indicates whether stops were too loose / entries too early.
    /// </summary>
    public double? MaxAdverseExcursionPercent { get; set; }

    // ── Entry signal context (only set on Sell/Stop trades) ──────────────────
    /// <summary>AlignedRanges value of the LTF confluence at entry (higher = stronger signal).</summary>
    public int? EntryAlignedRanges { get; set; }
    /// <summary>TrendCountSum of the LTF confluence at entry.</summary>
    public int? EntryTrendCountSum { get; set; }
}

public class BacktestSummary
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TimestampsEvaluated { get; set; }
    public decimal? FirstPrice { get; set; }
    public decimal? LastPrice { get; set; }
    public int TotalBuys { get; set; }
    public int TotalSells { get; set; }
    /// <summary>Kumulierter Gewinn aller abgeschlossenen Kauf-Verkauf-Paare in Prozent (nicht kompoundiert).</summary>
    public double? TotalProfitPercent { get; set; }
    /// <summary>Anteil profitabler Verkäufe in Prozent.</summary>
    public double? WinRatePercent { get; set; }
    /// <summary>Größter einzelner Verlust (negativster ProfitPercent eines Sells).</summary>
    public double? MaxLossPercent { get; set; }
    /// <summary>Größter einzelner Gewinn (positivster ProfitPercent eines Sells).</summary>
    public double? MaxGainPercent { get; set; }
    /// <summary>Buy-and-Hold-Rendite über den gesamten Zeitraum in Prozent.</summary>
    public double? BuyAndHoldPercent { get; set; }
    /// <summary>
    /// Compoundierter Gesamtgewinn aller abgeschlossenen Trades (geometrisch).
    /// Im Gegensatz zu <see cref="TotalProfitPercent"/> (additiv) berücksichtigt dieser Wert
    /// die Aufzinsung: ∏(1 + r_i/100) − 1 × 100.
    /// </summary>
    public double? CompoundedProfitPercent { get; set; }
    /// <summary>
    /// Anteil der Zeit, in der die Strategie eine offene Position hielt (in Prozent der Gesamtperiode).
    /// Berechnet aus Einstiegs-/Ausstiegszeitpunkten der Trades.
    /// </summary>
    public double? TimeInMarketPercent { get; set; }
    /// <summary>
    /// Maximaler Drawdown der Strategie-Equity-Kurve in Prozent (Peak-to-Trough).
    /// Berücksichtigt unrealisierte Verluste während offener Positionen.
    /// </summary>
    public double? MaxDrawdownPercent { get; set; }
    /// <summary>
    /// Maximaler Drawdown einer reinen Buy-and-Hold-Strategie über denselben Zeitraum.
    /// Ermöglicht fairen Vergleich: Strategy-MaxDrawdown vs. HODL-MaxDrawdown.
    /// </summary>
    public double? HodlMaxDrawdownPercent { get; set; }
    public List<BacktestTrade> Trades { get; set; } = [];
}

/// <summary>
/// Ergebnis einer Out-of-Sample-Split-Validierung.
/// Vergleicht die Strategie-Performance auf dem In-Sample-Zeitraum (startDate→splitDate)
/// mit dem Out-of-Sample-Zeitraum (splitDate→endDate), um Overfitting sichtbar zu machen.
/// </summary>
public class BacktestSplitValidationResult
{
    public DateTime SplitDate { get; set; }
    public BacktestSummary InSample { get; set; } = new();
    public BacktestSummary OutOfSample { get; set; } = new();

    /// <summary>Strategie-Rendite minus Buy-and-Hold-Rendite im In-Sample-Zeitraum.</summary>
    public double? InSampleStrategyVsHodlDelta { get; set; }

    /// <summary>Strategie-Rendite minus Buy-and-Hold-Rendite im Out-of-Sample-Zeitraum.</summary>
    public double? OutOfSampleStrategyVsHodlDelta { get; set; }

    /// <summary>
    /// Degradation des Strategy-vs-HODL-Vorteils: InSampleDelta − OutOfSampleDelta.
    /// Positiver Wert = Strategie verliert OOS an Überrendite → Overfitting-Hinweis.
    /// Negativer Wert = Strategie verbessert sich OOS gegenüber HODL.
    /// </summary>
    public double? StrategyVsHodlDegradation { get; set; }
}

/// <summary>
/// Ergebnis einer einzelnen Gate-Ablation-Messung.
/// Jede Zeile beschreibt, was passiert wenn genau ein Gate deaktiviert wird,
/// alle anderen Gates bleiben auf ihren appsettings-Produktionswerten.
/// </summary>
public record BacktestGateAblationRow(
    string GateName,
    string ProductionValue,
    string DisabledValue,
    double? InSampleStrategyVsHodlDelta,
    double? OutOfSampleStrategyVsHodlDelta,
    double? StrategyVsHodlDegradation,
    double? OosDeltaChangeVsBaseline,
    string Verdict
);

public record BacktestDiagnosticsCountRow(string Key, int Count);

public class BacktestDecisionDiagnostics
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TimestampsEvaluated { get; set; }

    public int RawBuySignals { get; set; }
    public int RawSellSignals { get; set; }
    public int RawWaitSignals { get; set; }

    public int PostRegimeBuySignals { get; set; }
    public int PostRegimeSellSignals { get; set; }
    public int PostRegimeWaitSignals { get; set; }

    public int RegimeBullSamples { get; set; }
    public int RegimeBearSamples { get; set; }
    public int RegimeNeutralSamples { get; set; }

    public int ExecutedBuys { get; set; }
    public int ExecutedSells { get; set; }
    public int HardStopLossTriggers { get; set; }

    public int BlockedBuyDueToOpenPosition { get; set; }
    public int BlockedBuyDueToFollowThrough { get; set; }
    public int BlockedBuyDueToBearMarket { get; set; }
    public int BlockedBuyDueToCooldown { get; set; }
    public int BlockedSellDueToNoOpenPosition { get; set; }
    public int BlockedSellDueToFollowThrough { get; set; }

    public int PersistedBuyLogsInRange { get; set; }
    public int PersistedSellLogsInRange { get; set; }

    public List<BacktestDiagnosticsCountRow> DecisionSourceCounts { get; set; } = [];
    public List<BacktestDiagnosticsCountRow> AlignedRangesRawCounts { get; set; } = [];
    public List<BacktestDiagnosticsCountRow> TrendCountSumRawCounts { get; set; } = [];
    public List<BacktestDiagnosticsCountRow> BullishTupleRawCounts { get; set; } = [];
    public List<BacktestDiagnosticsCountRow> BearishTupleRawCounts { get; set; } = [];
}

public record BacktestWalkForwardFoldRow(
    int Fold,
    DateTime TrainStart,
    DateTime TrainEnd,
    DateTime TestStart,
    DateTime TestEnd,
    double BestMomentumBrakeMaxRatio,
    double BestTroughMinDrawdownPercent,
    double BestTroughRecoveryProximityPercent,
    double BestBearishRegimeMinLinearBullishScore,
    double? TrainTotalProfitPercent,
    double? TrainBuyAndHoldPercent,
    double? TrainDelta,
    double? TestTotalProfitPercent,
    double? TestBuyAndHoldPercent,
    double? TestDelta,
    int TestTotalBuys,
    int TestTotalSells,
    double? TestWinRatePercent,
    double? TestMaxLossPercent,
    double? TestMaxGainPercent,
    int BestBullishMinAlignedRanges = LongTimeframeThresholds.BullishMinAlignedRanges,
    int BestBullishMaxAlignedRanges = int.MaxValue,
    int BestBullishMinAlignedAnchorRanges = LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
    int BestBullishMinTrendCountSum = LongTimeframeThresholds.BullishMinTrendCountSum,
    int BestBullishMaxTrendCountSum = int.MaxValue,
    int BestBearishMinAlignedRanges = LongTimeframeThresholds.BearishMinAlignedRanges,
    int BestBearishMinTrendCountSum = LongTimeframeThresholds.BearishMinTrendCountSum,
    bool BestBullishRegimeBlockBuy = false,
    bool BestEntryRequiresPriceAboveSma200 = false,
    double BestBreakevenTriggerPercent = 0.0);

/// <summary>
/// A single raw BUY signal sample with forward-return labels at multiple horizons.
/// Used for signal-level correlation analysis (AR, TC vs. outcome) with hundreds of
/// data points rather than the handful of executed trades.
/// </summary>
public class BuySignalSample
{
    public DateTime Timestamp { get; set; }
    public int AlignedRanges { get; set; }
    public int TrendCountSum { get; set; }
    public decimal Price { get; set; }
    public string Regime { get; set; } = string.Empty;
    /// <summary>Forward return % at +5 calendar days from signal timestamp. Null if no price available.</summary>
    public double? Return5d { get; set; }
    /// <summary>Forward return % at +14 calendar days.</summary>
    public double? Return14d { get; set; }
    /// <summary>Forward return % at +30 calendar days.</summary>
    public double? Return30d { get; set; }
    /// <summary>Forward return % at +90 calendar days.</summary>
    public double? Return90d { get; set; }
    /// <summary>Forward return % at +180 calendar days.</summary>
    public double? Return180d { get; set; }
}

public class BuySignalForwardReturnAnalysis
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    /// <summary>Total raw BUY signals emitted by the confluence engine (before any execution gating).</summary>
    public int TotalRawBuySignals { get; set; }
    public List<BuySignalSample> Samples { get; set; } = [];
}

/// <summary>
/// Ergebnis einer Regime-Stabilitaetsanalyse: testet die Strategie mit fixen Produktions-Parametern
/// ueber mehrere rollende Zeitfenster, um Konsistenz ueber verschiedene Marktphasen zu messen.
/// </summary>
public class RegimeStabilityResult
{
    public DateTime FullStart { get; set; }
    public DateTime FullEnd { get; set; }
    public int WindowMonths { get; set; }
    public int StepMonths { get; set; }
    public List<RegimeStabilityFold> Folds { get; set; } = new();

    public int WindowCount { get; set; }

    /// <summary>Fenster mit mindestens einem abgeschlossenen Trade.</summary>
    public int WindowsWithTrades { get; set; }
    /// <summary>Davon: Fenster mit positivem Gesamtgewinn.</summary>
    public int WindowsWithTradesPositive { get; set; }
    /// <summary>Davon: Fenster, die Buy-and-Hold schlagen.</summary>
    public int WindowsWithTradesBeatBnH { get; set; }

    /// <summary>Fenster ohne Trades, in denen Buy-and-Hold negativ war (korrekt gemieden).</summary>
    public int WindowsNoTradesCorrectAvoidance { get; set; }
    /// <summary>Fenster ohne Trades, in denen Buy-and-Hold positiv war (verpasste Chance).</summary>
    public int WindowsNoTradesMissedOpportunity { get; set; }
    /// <summary>Fenster mit Kauf-Einstieg, aber Haltedauer ueberschreitet Fensterende (offen bei Fensterablauf).</summary>
    public int WindowsWithOpenPosition { get; set; }

    /// <summary>Median-Delta (Strategie - BnH) ueber alle Fenster mit Trades.</summary>
    public double? MedianDeltaVsBuyAndHold { get; set; }

    /// <summary>Klartext-Urteil zur Stabilitaet ueber Marktphasen.</summary>
    public string Verdict { get; set; } = string.Empty;
}

public class RegimeStabilityFold
{
    public int Fold { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public int TotalBuys { get; set; }
    public int TotalSells { get; set; }
    public double? TotalProfitPercent { get; set; }
    public double? BuyAndHoldPercent { get; set; }
    /// <summary>TotalProfitPercent - BuyAndHoldPercent. Positiv = Strategie schlaegt BnH.</summary>
    public double? DeltaVsBuyAndHold { get; set; }
    public bool BeatsBuyAndHold { get; set; }
    public bool IsPositive { get; set; }
    /// <summary>Kontextnotiz, z. B. "Kein Trade, BnH -55% (korrekt gemieden)" oder "Kein Trade, BnH +120% (verpasst)".</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Ergebnis des Stationary-Block-Bootstraps (Politis &amp; Romano 1994):
/// Konfidenzintervalle fuer Strategie- und BnH-Performance unter Erhalt der
/// Autokorrelationsstruktur der Tagesrenditen, plus p-Wert fuer die Hypothese
/// "Strategie-Alpha &lt;= 0".
/// </summary>
public class BacktestBootstrapResult
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int Iterations { get; set; }
    public int TradingDays { get; set; }
    /// <summary>Erwartete mittlere Blocklaenge (geometrische Verteilung).</summary>
    public int MeanBlockLength { get; set; }
    /// <summary>Echtes Ergebnis der Strategie (compounded, %) im Zeitraum.</summary>
    public double ActualStrategyCompoundedPercent { get; set; }
    /// <summary>Echtes Buy-and-Hold-Ergebnis (compounded, %) im Zeitraum.</summary>
    public double ActualBuyAndHoldCompoundedPercent { get; set; }
    /// <summary>Realisiertes Alpha = Strategie - BnH (compounded, %).</summary>
    public double ActualAlphaPercent { get; set; }
    /// <summary>Anteil der Tage, an denen die Strategie investiert war (%).</summary>
    public double TimeInMarketPercent { get; set; }
    /// <summary>Verteilung der Strategie-Total-Returns (compounded, %) ueber die Bootstrap-Stichproben.</summary>
    public NullDistributionStats StrategyDistribution { get; set; } = new();
    /// <summary>Verteilung der BnH-Total-Returns (compounded, %) ueber die Bootstrap-Stichproben.</summary>
    public NullDistributionStats BuyAndHoldDistribution { get; set; } = new();
    /// <summary>Verteilung des Alphas (Strategie - BnH, in % compounded) ueber die Bootstrap-Stichproben.</summary>
    public NullDistributionStats AlphaDistribution { get; set; } = new();
    /// <summary>p-Wert fuer H0: "Alpha &lt;= 0" - Anteil der Bootstrap-Iterationen mit Alpha &lt;= 0.</summary>
    public double AlphaPValue { get; set; }
    public string Verdict { get; set; } = string.Empty;
}

