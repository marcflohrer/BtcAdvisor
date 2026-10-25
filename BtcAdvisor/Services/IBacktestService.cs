using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public interface IBacktestService
{
    /// <summary>
    /// Simuliert die Strategie über den angegebenen Zeitraum und gibt Performance-Metriken zurück.
    /// </summary>
    /// <param name="startDate">Startdatum (UTC).</param>
    /// <param name="endDate">Enddatum (UTC).</param>
    /// <param name="persist">Wenn <c>true</c>, werden erkannte Signale zusätzlich in BuyActionLogs/SellActionLogs persistiert (Signal-Backfill).</param>
    /// <param name="momentumBrakeMaxRatio">Maximales Verhältnis für den Momentum-Brake-Exit.</param>
    /// <param name="troughMinDrawdownPercent">Minimale Drawdown-Schwelle für den Intermediate-Trough-Exit.</param>
    /// <param name="troughRecoveryProximityPercent">Erforderliche Erholung zum Zyklus-Hoch für den Intermediate-Trough-Exit.</param>
    /// <param name="cancellationToken">Abbruchtoken.</param>
    Task<BacktestSummary> RunBacktestAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        bool persist = false,
        double momentumBrakeMaxRatio = DistributionTopThresholds.MomentumBrakeMaxRatio,
        double troughMinDrawdownPercent = DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
        double troughRecoveryProximityPercent = DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent);

    Task<BacktestSummary> RunBacktestAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        bool persist,
        double momentumBrakeMaxRatio,
        double troughMinDrawdownPercent,
        double troughRecoveryProximityPercent,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet);

    Task<BacktestSummary> RunBacktestAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        bool persist,
        double momentumBrakeMaxRatio,
        double troughMinDrawdownPercent,
        double troughRecoveryProximityPercent,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet,
        double bearishRegimeMinLinearBullishScore,
        int reEntryCooldownHours = 0,
        int smaExitDebounceDays = 0,
        bool bullishRegimeBlockBuy = false,
        bool entryRequiresPriceAboveSma200 = false,
        double breakevenTriggerPercent = 0.0,
        double trailingStopPercent = 0.0,
        double trailingStopActivationPercent = 0.0,
        double nearAthSellSuppressMinPercent = 0.0,
        int athProximityBuyBlockMaxAthAgeDays = 0,
        bool macroOnlyMode = true);

    /// <summary>
    /// Führt eine Out-of-Sample-Split-Validierung durch, um Overfitting zu erkennen.
    /// Teilt den Zeitraum an <paramref name="splitDate"/> in In-Sample (startDate→splitDate)
    /// und Out-of-Sample (splitDate→endDate) und vergleicht die Performance.
    /// </summary>
    Task<BacktestSplitValidationResult> RunSplitValidationAsync(
        DateTime startDate,
        DateTime splitDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        bool macroOnlyMode = true);

    /// <summary>
    /// Gate-Ablation-Sweep: misst den OOS-Effekt jedes einzelnen aktiven Gates
    /// durch Deaktivierung eines Gates auf einmal (alle anderen bleiben auf Produktionswerten).
    /// Gibt Zeilen per <see cref="IAsyncEnumerable{T}"/> zurück; erste Zeile ist immer die Baseline.
    /// </summary>
    IAsyncEnumerable<BacktestGateAblationRow> RunGateAblationAsync(
        DateTime startDate,
        DateTime splitDate,
        DateTime endDate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Führt einen Grid-Sweep über alle Threshold-Kombinationen durch.
    /// Gibt Ergebnisse per <see cref="IAsyncEnumerable{T}"/> zurück, sodass der HTTP-Client
    /// während der Verarbeitung kontinuierlich Daten empfängt (kein Timeout).
    /// </summary>
    IAsyncEnumerable<BacktestSweepRow> SweepThresholdsAsync(
        DateTime startDate,
        DateTime endDate,
        IReadOnlyList<double> momentumBrakeRatios,
        IReadOnlyList<double> troughMinDrawdownPercents,
        IReadOnlyList<double> troughRecoveryProximityPercents,
        CancellationToken cancellationToken);

    IAsyncEnumerable<BacktestSweepRow> SweepThresholdsAsync(
        DateTime startDate,
        DateTime endDate,
        IReadOnlyList<double> momentumBrakeRatios,
        IReadOnlyList<double> troughMinDrawdownPercents,
        IReadOnlyList<double> troughRecoveryProximityPercents,
        IReadOnlyList<int> bullishMinAlignedRanges,
        IReadOnlyList<int> bullishMinTrendCountSums,
        IReadOnlyList<int> bearishMinAlignedRanges,
        IReadOnlyList<int> bearishMinTrendCountSums,
        CancellationToken cancellationToken);

    IAsyncEnumerable<BacktestSweepRow> SweepThresholdsAsync(
        DateTime startDate,
        DateTime endDate,
        IReadOnlyList<double> momentumBrakeRatios,
        IReadOnlyList<double> troughMinDrawdownPercents,
        IReadOnlyList<double> troughRecoveryProximityPercents,
        IReadOnlyList<int> bullishMinAlignedRanges,
        IReadOnlyList<int> bullishMaxAlignedRanges,
        IReadOnlyList<int> bullishMinAlignedAnchorRanges,
        IReadOnlyList<int> bullishMinTrendCountSums,
        IReadOnlyList<int> bullishMaxTrendCountSums,
        IReadOnlyList<int> bearishMinAlignedRanges,
        IReadOnlyList<int> bearishMinTrendCountSums,
        IReadOnlyList<double> bearishRegimeMinLinearBullishScores,
        IReadOnlyList<bool> bullishRegimeBlockBuys,
        IReadOnlyList<bool> entryRequiresPriceAboveSma200s,
        IReadOnlyList<double> breakevenTriggerPercents,
        CancellationToken cancellationToken);

    IAsyncEnumerable<BacktestWalkForwardFoldRow> WalkForwardAsync(
        DateTime startDate,
        DateTime endDate,
        int trainMonths,
        int testMonths,
        int stepMonths,
        IReadOnlyList<double> momentumBrakeRatios,
        IReadOnlyList<double> troughMinDrawdownPercents,
        IReadOnlyList<double> troughRecoveryProximityPercents,
        CancellationToken cancellationToken);

    IAsyncEnumerable<BacktestWalkForwardFoldRow> WalkForwardAsync(
        DateTime startDate,
        DateTime endDate,
        int trainMonths,
        int testMonths,
        int stepMonths,
        IReadOnlyList<double> momentumBrakeRatios,
        IReadOnlyList<double> troughMinDrawdownPercents,
        IReadOnlyList<double> troughRecoveryProximityPercents,
        IReadOnlyList<int> bullishMinAlignedRanges,
        IReadOnlyList<int> bullishMinTrendCountSums,
        IReadOnlyList<int> bearishMinAlignedRanges,
        IReadOnlyList<int> bearishMinTrendCountSums,
        CancellationToken cancellationToken);

    IAsyncEnumerable<BacktestWalkForwardFoldRow> WalkForwardAsync(
        DateTime startDate,
        DateTime endDate,
        int trainMonths,
        int testMonths,
        int stepMonths,
        IReadOnlyList<double> momentumBrakeRatios,
        IReadOnlyList<double> troughMinDrawdownPercents,
        IReadOnlyList<double> troughRecoveryProximityPercents,
        IReadOnlyList<int> bullishMinAlignedRanges,
        IReadOnlyList<int> bullishMaxAlignedRanges,
        IReadOnlyList<int> bullishMinAlignedAnchorRanges,
        IReadOnlyList<int> bullishMinTrendCountSums,
        IReadOnlyList<int> bullishMaxTrendCountSums,
        IReadOnlyList<int> bearishMinAlignedRanges,
        IReadOnlyList<int> bearishMinTrendCountSums,
        IReadOnlyList<double> bearishRegimeMinLinearBullishScores,
        IReadOnlyList<bool> bullishRegimeBlockBuys,
        IReadOnlyList<bool> entryRequiresPriceAboveSma200s,
        IReadOnlyList<double> breakevenTriggerPercents,
        CancellationToken cancellationToken);

    Task<BacktestDecisionDiagnostics> GetDecisionDiagnosticsAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        double momentumBrakeMaxRatio = DistributionTopThresholds.MomentumBrakeMaxRatio,
        double troughMinDrawdownPercent = DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
        double troughRecoveryProximityPercent = DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent);

    Task<BacktestDecisionDiagnostics> GetDecisionDiagnosticsAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        double momentumBrakeMaxRatio,
        double troughMinDrawdownPercent,
        double troughRecoveryProximityPercent,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet);

    Task<BacktestDecisionDiagnostics> GetDecisionDiagnosticsAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        double momentumBrakeMaxRatio,
        double troughMinDrawdownPercent,
        double troughRecoveryProximityPercent,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet,
        double bearishRegimeMinLinearBullishScore);

    Task<BuySignalForwardReturnAnalysis> GetBuySignalForwardReturnsAsync(
        DateTime startDate,
        DateTime endDate,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet,
        CancellationToken cancellationToken);
}
