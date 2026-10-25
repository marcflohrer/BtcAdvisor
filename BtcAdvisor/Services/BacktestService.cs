using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;
using BtcAdvisor.Constants;
using Microsoft.Extensions.Options;

namespace BtcAdvisor.Services;

public class BacktestService(
    IBitcoinTradeAdvisor bitcoinTradeAdvisor,
    IStockCandleRepository stockCandleRepository,
    IBinanceKlineService binanceKlineService,
    IMarketRegimeService marketRegimeService,
    IBacktestRiskControlService riskControlService,
    IOptions<RiskControlOptions> riskControlOptions,
    ITradingRepository tradingRepository,
    IAtrLookupService atrLookupService,
    IMarketContextService marketContextService,
    IOptions<VolumeVolatilityOptions> volumeVolatilityOptions,
    IBearMarketFilterService bearMarketFilterService,
    IOptions<BearMarketFilterOptions> bearMarketFilterOptions) : IBacktestService
{
    private const string MicrostrategySymbol = "MSTR";

    public async Task<BacktestSummary> RunBacktestAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        bool persist = false,
        double momentumBrakeMaxRatio = DistributionTopThresholds.MomentumBrakeMaxRatio,
        double troughMinDrawdownPercent = DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
        double troughRecoveryProximityPercent = DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent)
    {
        return await RunBacktestAsync(
            startDate,
            endDate,
            cancellationToken,
            persist,
            momentumBrakeMaxRatio,
            troughMinDrawdownPercent,
            troughRecoveryProximityPercent,
            LongTimeframeConfluenceThresholdSet.Default,
            MarketRegimeThresholds.BearishRegimeMinLinearBullishScore);
    }

    public async Task<BacktestSummary> RunBacktestAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        bool persist,
        double momentumBrakeMaxRatio,
        double troughMinDrawdownPercent,
        double troughRecoveryProximityPercent,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet)
    {
        return await RunBacktestAsync(
            startDate,
            endDate,
            cancellationToken,
            persist,
            momentumBrakeMaxRatio,
            troughMinDrawdownPercent,
            troughRecoveryProximityPercent,
            confluenceThresholdSet,
            MarketRegimeThresholds.BearishRegimeMinLinearBullishScore);
    }

    public async Task<BacktestSummary> RunBacktestAsync(
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
        bool macroOnlyMode = false)
    {
        var timestamps = await GetBacktestTimestampsAsync(startDate, endDate, cancellationToken);
        if (timestamps.Count == 0)
            return new BacktestSummary { StartDate = startDate, EndDate = endDate };
        var mstrCandles = await stockCandleRepository.GetStockCandlesUpToAsync(MicrostrategySymbol, endDate, cancellationToken);
        return await RunBacktestCoreAsync(timestamps, mstrCandles, startDate, endDate, persist,
            momentumBrakeMaxRatio, troughMinDrawdownPercent, troughRecoveryProximityPercent,
            confluenceThresholdSet, bearishRegimeMinLinearBullishScore, cancellationToken,
            reEntryCooldownHoursOverride: reEntryCooldownHours,
            smaExitDebounceDaysOverride: smaExitDebounceDays,
            bullishRegimeBlockBuy: bullishRegimeBlockBuy,
            entryRequiresPriceAboveSma200: entryRequiresPriceAboveSma200,
            breakevenTriggerPercent: breakevenTriggerPercent,
            trailingStopPercentOverride: trailingStopPercent,
            trailingStopActivationPercentOverride: trailingStopActivationPercent,
            nearAthSellSuppressMinPercentOverride: nearAthSellSuppressMinPercent,
            athProximityBuyBlockMaxAthAgeDaysOverride: athProximityBuyBlockMaxAthAgeDays,
            macroOnlyMode: macroOnlyMode);
    }

    public async Task<BacktestSplitValidationResult> RunSplitValidationAsync(
        DateTime startDate,
        DateTime splitDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        bool macroOnlyMode = true)
    {
        var inSample = await RunBacktestAsync(startDate, splitDate, cancellationToken, persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            macroOnlyMode: macroOnlyMode);

        var outOfSample = await RunBacktestAsync(splitDate, endDate, cancellationToken, persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            macroOnlyMode: macroOnlyMode);

        var inSampleDelta = inSample.TotalProfitPercent.HasValue && inSample.BuyAndHoldPercent.HasValue
            ? inSample.TotalProfitPercent - inSample.BuyAndHoldPercent
            : (double?)null;
        var oosDelta = outOfSample.TotalProfitPercent.HasValue && outOfSample.BuyAndHoldPercent.HasValue
            ? outOfSample.TotalProfitPercent - outOfSample.BuyAndHoldPercent
            : (double?)null;

        return new BacktestSplitValidationResult
        {
            SplitDate = splitDate,
            InSample = inSample,
            OutOfSample = outOfSample,
            InSampleStrategyVsHodlDelta = inSampleDelta,
            OutOfSampleStrategyVsHodlDelta = oosDelta,
            StrategyVsHodlDegradation = inSampleDelta.HasValue && oosDelta.HasValue
                ? inSampleDelta - oosDelta
                : null
        };
    }

    /// <summary>
    /// Führt eine Split-Validierung mit explizit gesetzten Gate-Overrides durch.
    /// Sentinel-Werte: nearAthSellSuppressMinPercent=0 → appsettings, >100 → disabled.
    /// athProximityBuyBlockMaxAthAgeDays=0 → appsettings, -1 → disabled.
    /// smaExitDebounceDays=-1 → appsettings (7d), 0 → disabled.
    /// reEntryCooldownHours=-1 → appsettings (336h), 0 → disabled.
    /// </summary>
    private async Task<(double? IsDelta, double? OosDelta, double? Degradation)> RunSplitWithGatesAsync(
        DateTime startDate,
        DateTime splitDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        double nearAthSellSuppressMinPercent = 0.0,
        int athProximityBuyBlockMaxAthAgeDays = 0,
        int smaExitDebounceDays = -1,
        int reEntryCooldownHours = -1)
    {
        var inSample = await RunBacktestAsync(startDate, splitDate, cancellationToken, persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            reEntryCooldownHours: reEntryCooldownHours,
            smaExitDebounceDays: smaExitDebounceDays,
            nearAthSellSuppressMinPercent: nearAthSellSuppressMinPercent,
            athProximityBuyBlockMaxAthAgeDays: athProximityBuyBlockMaxAthAgeDays,
            macroOnlyMode: true);

        var outOfSample = await RunBacktestAsync(splitDate, endDate, cancellationToken, persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            reEntryCooldownHours: reEntryCooldownHours,
            smaExitDebounceDays: smaExitDebounceDays,
            nearAthSellSuppressMinPercent: nearAthSellSuppressMinPercent,
            athProximityBuyBlockMaxAthAgeDays: athProximityBuyBlockMaxAthAgeDays,
            macroOnlyMode: true);

        var isDelta = inSample.TotalProfitPercent.HasValue && inSample.BuyAndHoldPercent.HasValue
            ? inSample.TotalProfitPercent - inSample.BuyAndHoldPercent : (double?)null;
        var oosDelta = outOfSample.TotalProfitPercent.HasValue && outOfSample.BuyAndHoldPercent.HasValue
            ? outOfSample.TotalProfitPercent - outOfSample.BuyAndHoldPercent : (double?)null;
        var degradation = isDelta.HasValue && oosDelta.HasValue ? isDelta - oosDelta : (double?)null;
        return (isDelta, oosDelta, degradation);
    }

    public async IAsyncEnumerable<BacktestGateAblationRow> RunGateAblationAsync(
        DateTime startDate,
        DateTime splitDate,
        DateTime endDate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Sentinel values that resolve to production appsettings:
        //   nearAthSellSuppressMinPercent = 0.0  → appsettings NearAthSellSuppressMinPercent (80%)
        //   athProximityBuyBlockMaxAthAgeDays = 0 → appsettings AthProximityBuyBlockMaxAthAgeDays (180)
        //   smaExitDebounceDays = -1              → appsettings SmaExitDebounceDays (7)
        //   reEntryCooldownHours = -1             → appsettings ReEntryCooldownHours (336)
        const double nearAthProd = 0.0;
        const int athProxProd = 0;
        const int smaDebProd = -1;
        const int cooldownProd = -1;

        // Baseline: all gates at appsettings production values
        var (baseIsDelta, baseOosDelta, baseDegradation) = await RunSplitWithGatesAsync(
            startDate, splitDate, endDate, cancellationToken,
            nearAthProd, athProxProd, smaDebProd, cooldownProd);

        yield return new BacktestGateAblationRow(
            "Baseline", "all gates active (appsettings)", "—",
            baseIsDelta, baseOosDelta, baseDegradation, 0.0, "Reference");

        // Gate 1: NearAthSellSuppressMinPercent (appsettings: 80%)
        // Disable by passing >100 (sentinel: disabled).
        cancellationToken.ThrowIfCancellationRequested();
        var (isDelta1, oosDelta1, deg1) = await RunSplitWithGatesAsync(
            startDate, splitDate, endDate, cancellationToken,
            nearAthSellSuppressMinPercent: 101.0, athProxProd, smaDebProd, cooldownProd);
        var oosChange1 = oosDelta1.HasValue && baseOosDelta.HasValue ? oosDelta1 - baseOosDelta : (double?)null;
        yield return new BacktestGateAblationRow(
            "NearAthSellSuppressMinPercent", "80% (appsettings)", "101.0 (disabled)",
            isDelta1, oosDelta1, deg1, oosChange1, ComputeAblationVerdict(oosChange1));

        // Gate 2: AthProximityBuyBlockMaxAthAgeDays (appsettings: 180d)
        // Disable by passing -1 (sentinel: gate disabled entirely).
        cancellationToken.ThrowIfCancellationRequested();
        var (isDelta2, oosDelta2, deg2) = await RunSplitWithGatesAsync(
            startDate, splitDate, endDate, cancellationToken,
            nearAthProd, athProximityBuyBlockMaxAthAgeDays: -1, smaDebProd, cooldownProd);
        var oosChange2 = oosDelta2.HasValue && baseOosDelta.HasValue ? oosDelta2 - baseOosDelta : (double?)null;
        yield return new BacktestGateAblationRow(
            "AthProximityBuyBlockMaxAthAgeDays", "180d (appsettings)", "-1 (disabled)",
            isDelta2, oosDelta2, deg2, oosChange2, ComputeAblationVerdict(oosChange2));

        // Gate 3: SmaExitDebounceDays (appsettings: 7d)
        // Disable by passing 0 (sentinel: no debounce).
        cancellationToken.ThrowIfCancellationRequested();
        var (isDelta3, oosDelta3, deg3) = await RunSplitWithGatesAsync(
            startDate, splitDate, endDate, cancellationToken,
            nearAthProd, athProxProd, smaExitDebounceDays: 0, cooldownProd);
        var oosChange3 = oosDelta3.HasValue && baseOosDelta.HasValue ? oosDelta3 - baseOosDelta : (double?)null;
        yield return new BacktestGateAblationRow(
            "SmaExitDebounceDays", "7d (appsettings)", "0 (disabled)",
            isDelta3, oosDelta3, deg3, oosChange3, ComputeAblationVerdict(oosChange3));

        // Gate 4: ReEntryCooldownHours (appsettings: 336h)
        // Disable by passing 0 (sentinel: no cooldown).
        cancellationToken.ThrowIfCancellationRequested();
        var (isDelta4, oosDelta4, deg4) = await RunSplitWithGatesAsync(
            startDate, splitDate, endDate, cancellationToken,
            nearAthProd, athProxProd, smaDebProd, reEntryCooldownHours: 0);
        var oosChange4 = oosDelta4.HasValue && baseOosDelta.HasValue ? oosDelta4 - baseOosDelta : (double?)null;
        yield return new BacktestGateAblationRow(
            "ReEntryCooldownHours", "336h (appsettings)", "0 (disabled)",
            isDelta4, oosDelta4, deg4, oosChange4, ComputeAblationVerdict(oosChange4));
    }

    private static string ComputeAblationVerdict(double? oosChange) => oosChange switch
    {
        > 5.0 => "RemoveCandidate",   // Removing gate improves OOS by >5%: gate hurts OOS
        < -5.0 => "KeepGate",          // Removing gate hurts OOS by >5%: gate genuinely helps
        _ => "Neutral"
    };

    public async IAsyncEnumerable<BacktestSweepRow> SweepThresholdsAsync(
        DateTime startDate,
        DateTime endDate,
        IReadOnlyList<double> momentumBrakeRatios,
        IReadOnlyList<double> troughMinDrawdownPercents,
        IReadOnlyList<double> troughRecoveryProximityPercents,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in SweepThresholdsAsync(
                           startDate,
                           endDate,
                           momentumBrakeRatios,
                           troughMinDrawdownPercents,
                           troughRecoveryProximityPercents,
                           [LongTimeframeThresholds.BullishMinAlignedRanges],
                           [LongTimeframeThresholds.BullishMinTrendCountSum],
                           [LongTimeframeThresholds.BearishMinAlignedRanges],
                           [LongTimeframeThresholds.BearishMinTrendCountSum],
                           [MarketRegimeThresholds.BearishRegimeMinLinearBullishScore],
                           cancellationToken))
        {
            yield return row;
        }
    }

    public async IAsyncEnumerable<BacktestSweepRow> SweepThresholdsAsync(
        DateTime startDate,
        DateTime endDate,
        IReadOnlyList<double> momentumBrakeRatios,
        IReadOnlyList<double> troughMinDrawdownPercents,
        IReadOnlyList<double> troughRecoveryProximityPercents,
        IReadOnlyList<int> bullishMinAlignedRanges,
        IReadOnlyList<int> bullishMinTrendCountSums,
        IReadOnlyList<int> bearishMinAlignedRanges,
        IReadOnlyList<int> bearishMinTrendCountSums,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in SweepThresholdsAsync(
                           startDate,
                           endDate,
                           momentumBrakeRatios,
                           troughMinDrawdownPercents,
                           troughRecoveryProximityPercents,
                           bullishMinAlignedRanges,
                           [int.MaxValue],
                           [LongTimeframeThresholds.BullishMinAlignedAnchorRanges],
                           bullishMinTrendCountSums,
                           [int.MaxValue],
                           bearishMinAlignedRanges,
                           bearishMinTrendCountSums,
                           [MarketRegimeThresholds.BearishRegimeMinLinearBullishScore],
                           [false],
                           [false],
                           [0.0],
                           cancellationToken))
        {
            yield return row;
        }
    }

    public async IAsyncEnumerable<BacktestSweepRow> SweepThresholdsAsync(
        DateTime startDate,
        DateTime endDate,
        IReadOnlyList<double> momentumBrakeRatios,
        IReadOnlyList<double> troughMinDrawdownPercents,
        IReadOnlyList<double> troughRecoveryProximityPercents,
        IReadOnlyList<int> bullishMinAlignedRanges,
        IReadOnlyList<int> bullishMinTrendCountSums,
        IReadOnlyList<int> bearishMinAlignedRanges,
        IReadOnlyList<int> bearishMinTrendCountSums,
        IReadOnlyList<double> bearishRegimeMinLinearBullishScores,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in SweepThresholdsAsync(
                           startDate,
                           endDate,
                           momentumBrakeRatios,
                           troughMinDrawdownPercents,
                           troughRecoveryProximityPercents,
                           bullishMinAlignedRanges,
                           [int.MaxValue],
                           [LongTimeframeThresholds.BullishMinAlignedAnchorRanges],
                           bullishMinTrendCountSums,
                           [int.MaxValue],
                           bearishMinAlignedRanges,
                           bearishMinTrendCountSums,
                           bearishRegimeMinLinearBullishScores,
                           [false],
                           [false],
                           [0.0],
                           cancellationToken))
        {
            yield return row;
        }
    }
    public async IAsyncEnumerable<BacktestSweepRow> SweepThresholdsAsync(
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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Fetch shared data once – shared across all combinations.
        // CollectStatistics in BitcoinTradeAdvisor is cached in IMemoryCache after the first combination,
        // so subsequent combinations run without DB fetches for already-seen timestamps.
        var timestamps = await GetBacktestTimestampsAsync(startDate, endDate, cancellationToken);
        var mstrCandles = await stockCandleRepository.GetStockCandlesUpToAsync(MicrostrategySymbol, endDate, cancellationToken);

        foreach (var momentumRatio in momentumBrakeRatios)
        {
            foreach (var trough in troughMinDrawdownPercents)
            {
                foreach (var proximity in troughRecoveryProximityPercents)
                {
                    foreach (var bullishAligned in bullishMinAlignedRanges)
                    {
                        foreach (var bullishMaxAligned in bullishMaxAlignedRanges)
                        {
                            foreach (var bullishAnchor in bullishMinAlignedAnchorRanges)
                            {
                            foreach (var bullishTrend in bullishMinTrendCountSums)
                            {
                                foreach (var bullishMaxTrend in bullishMaxTrendCountSums)
                                {
                                    foreach (var bearishAligned in bearishMinAlignedRanges)
                                    {
                                        foreach (var bearishTrend in bearishMinTrendCountSums)
                                        {
                                            foreach (var bearishRegimeMinLinearBullishScore in bearishRegimeMinLinearBullishScores)
                                            {
                                                foreach (var blockBullRegimeBuy in bullishRegimeBlockBuys)
                                                {
                                                    foreach (var requireAboveSma200 in entryRequiresPriceAboveSma200s)
                                                    {
                                                        foreach (var breakevenPct in breakevenTriggerPercents)
                                                        {
                                                            cancellationToken.ThrowIfCancellationRequested();
                                                            var confluenceThresholdSet = new LongTimeframeConfluenceThresholdSet(
                                                                bullishMinAlignedRanges: bullishAligned,
                                                                bullishMaxAlignedRanges: bullishMaxAligned,
                                                                bullishMinAlignedAnchorRanges: bullishAnchor,
                                                                bullishMinTrendCountSum: bullishTrend,
                                                                bullishMaxTrendCountSum: bullishMaxTrend,
                                                                bearishMinAlignedRanges: bearishAligned,
                                                                bearishMinTrendCountSum: bearishTrend);

                                                            var summary = await RunBacktestCoreAsync(timestamps, mstrCandles, startDate, endDate,
                                                                persist: false, momentumBrakeMaxRatio: momentumRatio,
                                                                troughMinDrawdownPercent: trough, troughRecoveryProximityPercent: proximity,
                                                                confluenceThresholdSet, bearishRegimeMinLinearBullishScore,
                                                                cancellationToken,
                                                                bullishRegimeBlockBuy: blockBullRegimeBuy,
                                                                entryRequiresPriceAboveSma200: requireAboveSma200,
                                                                breakevenTriggerPercent: breakevenPct);

                                                            double? delta = summary.TotalProfitPercent.HasValue && summary.BuyAndHoldPercent.HasValue
                                                                ? Math.Round(summary.TotalProfitPercent.Value - summary.BuyAndHoldPercent.Value, 2)
                                                                : null;

                                                            yield return new BacktestSweepRow(
                                                                momentumRatio, trough, proximity, bearishRegimeMinLinearBullishScore,
                                                                summary.TotalProfitPercent, summary.BuyAndHoldPercent, delta,
                                                                summary.TotalBuys, summary.TotalSells,
                                                                summary.WinRatePercent, summary.MaxLossPercent, summary.MaxGainPercent,
                                                                bullishAligned,
                                                                bullishMaxAligned,
                                                                bullishAnchor,
                                                                bullishTrend,
                                                                bullishMaxTrend,
                                                                bearishAligned,
                                                                bearishTrend,
                                                                blockBullRegimeBuy,
                                                                requireAboveSma200,
                                                                breakevenPct);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            } // bullishMinAlignedAnchorRanges
                        }
                    }
                }
            }
        }
    }

    public async IAsyncEnumerable<BacktestWalkForwardFoldRow> WalkForwardAsync(
        DateTime startDate,
        DateTime endDate,
        int trainMonths,
        int testMonths,
        int stepMonths,
        IReadOnlyList<double> momentumBrakeRatios,
        IReadOnlyList<double> troughMinDrawdownPercents,
        IReadOnlyList<double> troughRecoveryProximityPercents,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in WalkForwardAsync(
                           startDate,
                           endDate,
                           trainMonths,
                           testMonths,
                           stepMonths,
                           momentumBrakeRatios,
                           troughMinDrawdownPercents,
                           troughRecoveryProximityPercents,
                           [LongTimeframeThresholds.BullishMinAlignedRanges],
                           [int.MaxValue],
                           [LongTimeframeThresholds.BullishMinAlignedAnchorRanges],
                           [LongTimeframeThresholds.BullishMinTrendCountSum],
                           [int.MaxValue],
                           [LongTimeframeThresholds.BearishMinAlignedRanges],
                           [LongTimeframeThresholds.BearishMinTrendCountSum],
                           [MarketRegimeThresholds.BearishRegimeMinLinearBullishScore],
                           [false],
                           [false],
                           [0.0],
                           cancellationToken))
        {
            yield return row;
        }
    }

    public async IAsyncEnumerable<BacktestWalkForwardFoldRow> WalkForwardAsync(
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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in WalkForwardAsync(
                           startDate,
                           endDate,
                           trainMonths,
                           testMonths,
                           stepMonths,
                           momentumBrakeRatios,
                           troughMinDrawdownPercents,
                           troughRecoveryProximityPercents,
                           bullishMinAlignedRanges,
                           [int.MaxValue],
                           [LongTimeframeThresholds.BullishMinAlignedAnchorRanges],
                           bullishMinTrendCountSums,
                           [int.MaxValue],
                           bearishMinAlignedRanges,
                           bearishMinTrendCountSums,
                           [MarketRegimeThresholds.BearishRegimeMinLinearBullishScore],
                           [false],
                           [false],
                           [0.0],
                           cancellationToken))
        {
            yield return row;
        }
    }

    public async IAsyncEnumerable<BacktestWalkForwardFoldRow> WalkForwardAsync(
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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (trainMonths <= 0) throw new ArgumentOutOfRangeException(nameof(trainMonths));
        if (testMonths <= 0) throw new ArgumentOutOfRangeException(nameof(testMonths));
        if (stepMonths <= 0) throw new ArgumentOutOfRangeException(nameof(stepMonths));
        if (endDate <= startDate) yield break;

        var fold = 0;
        var trainStart = startDate;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trainEndExclusive = trainStart.AddMonths(trainMonths);
            var testStart = trainEndExclusive;
            var testEndExclusive = testStart.AddMonths(testMonths);

            if (testEndExclusive > endDate)
            {
                yield break;
            }

            var trainEnd = trainEndExclusive.AddTicks(-1);
            var testEnd = testEndExclusive.AddTicks(-1);

            BacktestSweepRow? bestTrainRow = null;
            await foreach (var row in SweepThresholdsAsync(
                               trainStart,
                               trainEnd,
                               momentumBrakeRatios,
                               troughMinDrawdownPercents,
                               troughRecoveryProximityPercents,
                               bullishMinAlignedRanges,
                               bullishMaxAlignedRanges,
                               bullishMinAlignedAnchorRanges,
                               bullishMinTrendCountSums,
                               bullishMaxTrendCountSums,
                               bearishMinAlignedRanges,
                               bearishMinTrendCountSums,
                               bearishRegimeMinLinearBullishScores,
                               bullishRegimeBlockBuys,
                               entryRequiresPriceAboveSma200s,
                               breakevenTriggerPercents,
                               cancellationToken))
            {
                if (bestTrainRow is null)
                {
                    bestTrainRow = row;
                    continue;
                }

                var currentDelta = row.Delta ?? double.MinValue;
                var bestDelta = bestTrainRow.Delta ?? double.MinValue;
                if (currentDelta > bestDelta)
                {
                    bestTrainRow = row;
                }
            }

            if (bestTrainRow is null)
            {
                yield break;
            }

            var testSummary = await RunBacktestAsync(
                testStart,
                testEnd,
                cancellationToken,
                persist: false,
                momentumBrakeMaxRatio: bestTrainRow.MomentumBrakeMaxRatio,
                troughMinDrawdownPercent: bestTrainRow.TroughMinDrawdownPercent,
                troughRecoveryProximityPercent: bestTrainRow.TroughRecoveryProximityPercent,
                confluenceThresholdSet: new LongTimeframeConfluenceThresholdSet(
                    bullishMinAlignedRanges: bestTrainRow.BullishMinAlignedRanges,
                    bullishMaxAlignedRanges: bestTrainRow.BullishMaxAlignedRanges,
                    bullishMinAlignedAnchorRanges: bestTrainRow.BullishMinAlignedAnchorRanges,
                    bullishMinTrendCountSum: bestTrainRow.BullishMinTrendCountSum,
                    bullishMaxTrendCountSum: bestTrainRow.BullishMaxTrendCountSum,
                    bearishMinAlignedRanges: bestTrainRow.BearishMinAlignedRanges,
                    bearishMinTrendCountSum: bestTrainRow.BearishMinTrendCountSum),
                bearishRegimeMinLinearBullishScore: bestTrainRow.BearishRegimeMinLinearBullishScore,
                bullishRegimeBlockBuy: bestTrainRow.BullishRegimeBlockBuy,
                entryRequiresPriceAboveSma200: bestTrainRow.EntryRequiresPriceAboveSma200,
                breakevenTriggerPercent: bestTrainRow.BreakevenTriggerPercent);

            double? testDelta = testSummary.TotalProfitPercent.HasValue && testSummary.BuyAndHoldPercent.HasValue
                ? Math.Round(testSummary.TotalProfitPercent.Value - testSummary.BuyAndHoldPercent.Value, 2)
                : null;

            fold++;
            yield return new BacktestWalkForwardFoldRow(
                fold,
                trainStart,
                trainEnd,
                testStart,
                testEnd,
                bestTrainRow.MomentumBrakeMaxRatio,
                bestTrainRow.TroughMinDrawdownPercent,
                bestTrainRow.TroughRecoveryProximityPercent,
                bestTrainRow.BearishRegimeMinLinearBullishScore,
                bestTrainRow.TotalProfitPercent,
                bestTrainRow.BuyAndHoldPercent,
                bestTrainRow.Delta,
                testSummary.TotalProfitPercent,
                testSummary.BuyAndHoldPercent,
                testDelta,
                testSummary.TotalBuys,
                testSummary.TotalSells,
                testSummary.WinRatePercent,
                testSummary.MaxLossPercent,
                testSummary.MaxGainPercent,
                bestTrainRow.BullishMinAlignedRanges,
                bestTrainRow.BullishMaxAlignedRanges,
                bestTrainRow.BullishMinAlignedAnchorRanges,
                bestTrainRow.BullishMinTrendCountSum,
                bestTrainRow.BullishMaxTrendCountSum,
                bestTrainRow.BearishMinAlignedRanges,
                bestTrainRow.BearishMinTrendCountSum,
                bestTrainRow.BullishRegimeBlockBuy,
                bestTrainRow.EntryRequiresPriceAboveSma200,
                bestTrainRow.BreakevenTriggerPercent);

            trainStart = trainStart.AddMonths(stepMonths);
        }
    }

    public async Task<BuySignalForwardReturnAnalysis> GetBuySignalForwardReturnsAsync(
        DateTime startDate,
        DateTime endDate,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet,
        CancellationToken cancellationToken)
    {
        var timestamps = await GetBacktestTimestampsAsync(startDate, endDate, cancellationToken);
        var mstrCandles = await stockCandleRepository.GetStockCandlesUpToAsync(MicrostrategySymbol, endDate, cancellationToken);

        // Collect all prices and raw BUY signal events in a single forward pass.
        var priceTimeSeries = new List<(DateTime Ts, decimal Price)>(timestamps.Count);
        var rawSignalEvents = new List<(DateTime Ts, int Ar, int Tc, decimal Price, MarketRegime Regime)>();

        foreach (var timestamp in timestamps)
        {
            var (strategyHigh, strategyLow) = LookupMicrostrategyPrice(mstrCandles, timestamp);

            IReadOnlyCollection<BitcoinTradeStatus> results;
            try
            {
                // Pass null for all sim state — we want the raw confluence signal,
                // independent of any ongoing position.
                results = await GetBitcoinTradeAdviceAsync(
                    timestamp,
                    latestBuyPrice: null,
                    latestSellPrice: null,
                    strategyHigh,
                    strategyLow,
                    lowestBuyAfterFireSell: null,
                    highestPanicBuyAfterLowest: null,
                    confluenceThresholdSet,
                    cancellationToken);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (results.Count == 0) continue;

            var result = results.First();
            var price = (decimal)result.CurrentPrice;
            if (price == 0) continue;

            priceTimeSeries.Add((timestamp, price));

            if (result.TradeAdvice.Contains("Buy", StringComparison.OrdinalIgnoreCase))
            {
                var regime = (await marketRegimeService.GetRegimeAsync(timestamp, cancellationToken)).Regime;
                rawSignalEvents.Add((timestamp, result.AlignedRanges, result.TrendCountSum, price, regime));
            }
        }

        // Build a daily price lookup (last 2h bar of each calendar day).
        var dailyPrices = new Dictionary<DateOnly, decimal>(priceTimeSeries.Count / 12);
        foreach (var (ts, price) in priceTimeSeries)
        {
            dailyPrices[DateOnly.FromDateTime(ts)] = price;
        }

        var samples = new List<BuySignalSample>(rawSignalEvents.Count);
        foreach (var (ts, ar, tc, price, regime) in rawSignalEvents)
        {
            samples.Add(new BuySignalSample
            {
                Timestamp = ts,
                AlignedRanges = ar,
                TrendCountSum = tc,
                Price = price,
                Regime = regime.ToString(),
                Return5d = LookupForwardReturn(dailyPrices, ts, 5, price),
                Return14d = LookupForwardReturn(dailyPrices, ts, 14, price),
                Return30d = LookupForwardReturn(dailyPrices, ts, 30, price),
                Return90d = LookupForwardReturn(dailyPrices, ts, 90, price),
                Return180d = LookupForwardReturn(dailyPrices, ts, 180, price),
            });
        }

        return new BuySignalForwardReturnAnalysis
        {
            StartDate = startDate,
            EndDate = endDate,
            TotalRawBuySignals = rawSignalEvents.Count,
            Samples = samples
        };
    }

    private static double? LookupForwardReturn(
        Dictionary<DateOnly, decimal> dailyPrices,
        DateTime signalTs,
        int days,
        decimal entryPrice)
    {
        var targetDate = DateOnly.FromDateTime(signalTs).AddDays(days);
        // Allow up to 2 days of gap (weekends / sparse data).
        for (var offset = 0; offset <= 2; offset++)
        {
            if (dailyPrices.TryGetValue(targetDate.AddDays(offset), out var forwardPrice))
            {
                return PercentageService.GetPercentage(
                    (double)(forwardPrice - entryPrice),
                    (double)entryPrice);
            }
        }
        return null;
    }

    public async Task<BacktestDecisionDiagnostics> GetDecisionDiagnosticsAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        double momentumBrakeMaxRatio = DistributionTopThresholds.MomentumBrakeMaxRatio,
        double troughMinDrawdownPercent = DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
        double troughRecoveryProximityPercent = DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent)
    {
        return await GetDecisionDiagnosticsAsync(
            startDate,
            endDate,
            cancellationToken,
            momentumBrakeMaxRatio,
            troughMinDrawdownPercent,
            troughRecoveryProximityPercent,
            LongTimeframeConfluenceThresholdSet.Default);
    }

    public async Task<BacktestDecisionDiagnostics> GetDecisionDiagnosticsAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        double momentumBrakeMaxRatio,
        double troughMinDrawdownPercent,
        double troughRecoveryProximityPercent,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet)
    {
        return await GetDecisionDiagnosticsAsync(
            startDate,
            endDate,
            cancellationToken,
            momentumBrakeMaxRatio,
            troughMinDrawdownPercent,
            troughRecoveryProximityPercent,
            confluenceThresholdSet,
            MarketRegimeThresholds.BearishRegimeMinLinearBullishScore);
    }

    public async Task<BacktestDecisionDiagnostics> GetDecisionDiagnosticsAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken,
        double momentumBrakeMaxRatio,
        double troughMinDrawdownPercent,
        double troughRecoveryProximityPercent,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet,
        double bearishRegimeMinLinearBullishScore)
    {
        var timestamps = await GetBacktestTimestampsAsync(startDate, endDate, cancellationToken);
        var mstrCandles = await stockCandleRepository.GetStockCandlesUpToAsync(MicrostrategySymbol, endDate, cancellationToken);
        var candleMap = await BuildCandleMapAsync(endDate, cancellationToken);

        // CycleTop: Einmal vorab berechnen statt pro Timestamp (vermeidet 40k List-Allokationen und Timeouts)
        var (cycleTopMapDiag, _) = bearMarketFilterOptions.Value.CycleTopSellZoneEnabled
            ? await PrecomputeCycleTopMapAsync(endDate, timestamps, cancellationToken)
            : ((IReadOnlyDictionary<DateOnly, bool>)new Dictionary<DateOnly, bool>(),
               (IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());

        // PiCycleTop: Einmal vorab berechnen
        var piCycleTopMapDiag = bearMarketFilterOptions.Value.PiCycleTopSellEnabled
            ? await PrecomputePiCycleTopMapAsync(endDate, timestamps, cancellationToken)
            : (IReadOnlyDictionary<DateOnly, bool>)new Dictionary<DateOnly, bool>();

        decimal? simLatestBuyPrice = null;
        decimal? simLatestSellPrice = null;
        decimal? lastAcceptedBuySignalPrice = null;
        decimal? lastAcceptedSellSignalPrice = null;
        BuyActionLog? simLowestBuyAfterFireSell = null;
        BuyActionLog? simHighestPanicBuyAfterLowest = null;
        var simHasOpenPosition = false;
        var distributionState = new DistributionTopState();
        DateTime? lastSellTimestamp = null;
        decimal simTrailingHighWaterMark = 0m;

        var rawBuySignals = 0;
        var rawSellSignals = 0;
        var rawWaitSignals = 0;
        var postRegimeBuySignals = 0;
        var postRegimeSellSignals = 0;
        var postRegimeWaitSignals = 0;
        var regimeBullSamples = 0;
        var regimeBearSamples = 0;
        var regimeNeutralSamples = 0;
        var executedBuys = 0;
        var executedSells = 0;
        var hardStopLossTriggers = 0;
        var blockedBuyDueToOpenPosition = 0;
        var blockedBuyDueToFollowThrough = 0;
        var blockedBuyDueToBearMarket = 0;
        var blockedBuyDueToCooldown = 0;
        var blockedSellDueToNoOpenPosition = 0;
        var blockedSellDueToFollowThrough = 0;
        var decisionSourceCounts = new Dictionary<DecisionSource, int>();
        var alignedRangesRawCounts = new Dictionary<int, int>();
        var trendCountRawCounts = new Dictionary<int, int>();
        var bullishTupleRawCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var bearishTupleRawCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var timestamp in timestamps)
        {
            var (strategyHigh, strategyLow) = LookupMicrostrategyPrice(mstrCandles, timestamp);

            IReadOnlyCollection<BitcoinTradeStatus> results;
            try
            {
                results = await GetBitcoinTradeAdviceAsync(
                    timestamp,
                    simLatestBuyPrice,
                    simLatestSellPrice,
                    strategyHigh,
                    strategyLow,
                    simLowestBuyAfterFireSell,
                    simHighestPanicBuyAfterLowest,
                    confluenceThresholdSet,
                    cancellationToken);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (results.Count == 0)
            {
                continue;
            }

            var result = results.First();
            var price = (decimal)result.CurrentPrice;
            if (price == 0)
            {
                continue;
            }

            if (!decisionSourceCounts.TryAdd(result.DecisionSource, 1))
            {
                decisionSourceCounts[result.DecisionSource]++;
            }

            if (!alignedRangesRawCounts.TryAdd(result.AlignedRanges, 1))
            {
                alignedRangesRawCounts[result.AlignedRanges]++;
            }

            if (!trendCountRawCounts.TryAdd(result.TrendCountSum, 1))
            {
                trendCountRawCounts[result.TrendCountSum]++;
            }

            if (simHasOpenPosition)
            {
                distributionState.Tick(price);
                if (price > simTrailingHighWaterMark)
                {
                    simTrailingHighWaterMark = price;
                }
            }

            var advice = result.TradeAdvice;
            CountAdvice(advice, ref rawBuySignals, ref rawSellSignals, ref rawWaitSignals);

            if (advice.Contains("Buy", StringComparison.OrdinalIgnoreCase))
            {
                var key = BuildTupleKey(result.AlignedRanges, result.TrendCountSum);
                if (!bullishTupleRawCounts.TryAdd(key, 1))
                {
                    bullishTupleRawCounts[key]++;
                }
            }
            else if (IsSellAdvice(advice))
            {
                var key = BuildTupleKey(result.AlignedRanges, result.TrendCountSum);
                if (!bearishTupleRawCounts.TryAdd(key, 1))
                {
                    bearishTupleRawCounts[key]++;
                }
            }

            var atrStopEval = new StopLossEvaluation(false, 0m);
            decimal? barLow = candleMap.TryGetValue(timestamp, out var diagBar) ? (decimal)diagBar.Low : null;
            if (simLatestBuyPrice.HasValue && riskControlOptions.Value.EnableAtrStopLoss)
            {
                var atrValue = await atrLookupService.GetAtrAsync(
                    riskControlOptions.Value.AtrInterval,
                    timestamp,
                    riskControlOptions.Value.AtrPeriod,
                    cancellationToken);
                atrStopEval = riskControlService.EvaluateAtrStop(
                    simLatestBuyPrice.Value,
                    price,
                    barLow,
                    atrValue,
                    riskControlOptions.Value.AtrMultiplier,
                    riskControlOptions.Value.StopLossSlippagePercent);
            }

            var hardStopEval = new StopLossEvaluation(false, 0m);
            if (simLatestBuyPrice.HasValue && riskControlOptions.Value.EnableHardStopLoss)
            {
                hardStopEval = riskControlService.EvaluateHardStop(
                    simLatestBuyPrice.Value,
                    price,
                    barLow,
                    riskControlOptions.Value.HardStopLossPercent,
                    riskControlOptions.Value.StopLossSlippagePercent);
            }

            var trailStopEval = new StopLossEvaluation(false, 0m);
            if (simLatestBuyPrice.HasValue && riskControlOptions.Value.EnableTrailingStop)
            {
                trailStopEval = riskControlService.EvaluateTrailingStop(
                    simLatestBuyPrice.Value,
                    price,
                    simTrailingHighWaterMark,
                    riskControlOptions.Value.TrailingStopActivationPercent,
                    riskControlOptions.Value.TrailingStopPercent,
                    riskControlOptions.Value.StopLossSlippagePercent);
            }

            if (simLatestBuyPrice.HasValue && (atrStopEval.Triggered || hardStopEval.Triggered || trailStopEval.Triggered))
            {
                // Select best fill: highest exit price among triggered stops (most favourable for simulated P&L).
                var exitPrice = new[] { atrStopEval, hardStopEval, trailStopEval }
                    .Where(e => e.Triggered)
                    .Max(e => e.ExitPrice);
                hardStopLossTriggers++;
                executedSells++;
                distributionState.OnExit(simLatestBuyPrice.Value, exitPrice);
                simLatestSellPrice = exitPrice;
                lastAcceptedSellSignalPrice = exitPrice;
                lastAcceptedBuySignalPrice = null;
                simLatestBuyPrice = null;
                simHasOpenPosition = false;
                simTrailingHighWaterMark = 0m;
                simLowestBuyAfterFireSell = null;
                simHighestPanicBuyAfterLowest = null;
                lastSellTimestamp = timestamp;
                continue;
            }

            if (simHasOpenPosition
                && simLatestBuyPrice.HasValue
                && !advice.Contains("Sell")
                && DistributionTopFilter.ShouldTriggerDistributionSell(simLatestBuyPrice.Value, price, distributionState,
                    troughMinDrawdownPercent: troughMinDrawdownPercent,
                    momentumBrakeMaxRatio: momentumBrakeMaxRatio,
                    troughRecoveryProximityPercent: troughRecoveryProximityPercent))
            {
                advice = "DistributionSell";
                result.DecisionSource = DecisionSource.DistributionTop;
            }

            result.TradeAdvice = advice;
            var regime = (await marketRegimeService.GetRegimeAsync(timestamp, cancellationToken)).Regime;
            switch (regime)
            {
                case MarketRegime.Bull:
                    regimeBullSamples++;
                    break;
                case MarketRegime.Bear:
                    regimeBearSamples++;
                    break;
                default:
                    regimeNeutralSamples++;
                    break;
            }

            advice = await ApplyRegimeWithBonusAsync(result, regime, bearishRegimeMinLinearBullishScore, timestamp, cancellationToken);
            if (ShouldForceCycleTopSellFromMap(advice, timestamp, simHasOpenPosition, cycleTopMapDiag))
            {
                advice = "DistributionSell";
                result.TradeAdvice = advice;
                result.DecisionSource = DecisionSource.DistributionTop;
            }
            if (ShouldForceCycleTopSellFromMap(advice, timestamp, simHasOpenPosition, piCycleTopMapDiag))
            {
                advice = "DistributionSell";
                result.TradeAdvice = advice;
                result.DecisionSource = DecisionSource.DistributionTop;
            }
            CountAdvice(advice, ref postRegimeBuySignals, ref postRegimeSellSignals, ref postRegimeWaitSignals);

            if (advice.Contains("Buy"))
            {
                if (simHasOpenPosition)
                {
                    blockedBuyDueToOpenPosition++;
                    continue;
                }

                var passesBullish = SignalFollowThroughFilter.PassesBullish(
                    price,
                    lastAcceptedBuySignalPrice,
                    SignalFollowThroughThresholds.BullishMinFollowThroughPercent);

                if (!passesBullish)
                {
                    blockedBuyDueToFollowThrough++;
                    continue;
                }

                if (bearMarketFilterOptions.Value.Enabled
                    && await bearMarketFilterService.IsBearMarketAsync(
                        bearMarketFilterOptions.Value.Interval,
                        timestamp,
                        bearMarketFilterOptions.Value.MovingAveragePeriod,
                        cancellationToken)
                    && !(bearMarketFilterOptions.Value.AthDrawdownOverrideEnabled
                         && await bearMarketFilterService.IsDeepCycleLowAsync(
                             bearMarketFilterOptions.Value.AthDrawdownInterval,
                             timestamp,
                             AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(
                                 bearMarketFilterOptions.Value,
                                 timestamp),
                             cancellationToken))
                    && !(bearMarketFilterOptions.Value.PuellMultipleBuyBoostEnabled
                         && await bearMarketFilterService.IsPuellMultipleUndervaluedAsync(
                             bearMarketFilterOptions.Value.PuellMultipleInterval,
                             timestamp,
                             bearMarketFilterOptions.Value.PuellMultipleBuyBoostThreshold,
                             cancellationToken)))
                {
                    blockedBuyDueToBearMarket++;
                    continue;
                }

                var cooldownHours = riskControlOptions.Value.ReEntryCooldownHours;
                if (cooldownHours > 0
                    && lastSellTimestamp.HasValue
                    && (timestamp - lastSellTimestamp.Value).TotalHours < cooldownHours)
                {
                    blockedBuyDueToCooldown++;
                    continue;
                }

                executedBuys++;
                simLatestBuyPrice = price;
                lastAcceptedBuySignalPrice = price;
                lastAcceptedSellSignalPrice = null;
                simHasOpenPosition = true;
                simTrailingHighWaterMark = price;
                distributionState.OnEntry(price);

                if (simLowestBuyAfterFireSell == null || price < simLowestBuyAfterFireSell.Price)
                {
                    simLowestBuyAfterFireSell = new BuyActionLog { Price = price, TradingDate = timestamp, TradeAdvice = advice };
                    simHighestPanicBuyAfterLowest = null;
                }

                if (advice.Contains("Panic") && simLowestBuyAfterFireSell != null
                    && timestamp > simLowestBuyAfterFireSell.TradingDate)
                {
                    if (simHighestPanicBuyAfterLowest == null || price > simHighestPanicBuyAfterLowest.Price)
                    {
                        simHighestPanicBuyAfterLowest = new BuyActionLog { Price = price, TradingDate = timestamp, TradeAdvice = advice };
                    }
                }
            }
            else if (advice.Contains("Sell"))
            {
                if (!simHasOpenPosition || !simLatestBuyPrice.HasValue)
                {
                    blockedSellDueToNoOpenPosition++;
                    continue;
                }

                // Deep-cycle-low sell suppression: regular bearish exits are held in the zone.
                // Emergency exits (FireSell, HardStopLoss, TrailingStop) bypass this gate.
                if (bearMarketFilterOptions.Value.AthDrawdownSuppressBearishSellEnabled
                    && !advice.Contains("FireSell")
                    && !advice.Contains("HardStop")
                    && !advice.Contains("TrailingStop")
                    && await bearMarketFilterService.IsDeepCycleLowAsync(
                           bearMarketFilterOptions.Value.AthDrawdownSuppressInterval,
                        timestamp,
                        AthDrawdownThresholdPolicy.ResolveSuppressThresholdPercent(
                            bearMarketFilterOptions.Value,
                            timestamp),
                        cancellationToken))
                {
                    continue;
                }

                // Near-ATH sell suppression (see NearAthSellSuppressEnabled).
                if (bearMarketFilterOptions.Value.NearAthSellSuppressEnabled
                    && !advice.Contains("FireSell")
                    && !advice.Contains("HardStop")
                    && !advice.Contains("TrailingStop")
                    && await bearMarketFilterService.IsNearAthForSellSuppressAsync(
                        bearMarketFilterOptions.Value.NearAthSellSuppressInterval,
                        timestamp,
                        bearMarketFilterOptions.Value.NearAthSellSuppressMinPercent,
                        cancellationToken))
                {
                    continue;
                }

                var passesBearish = SignalFollowThroughFilter.PassesBearish(
                    price,
                    lastAcceptedSellSignalPrice,
                    SignalFollowThroughThresholds.BearishMinFollowThroughPercent);

                if (!passesBearish)
                {
                    blockedSellDueToFollowThrough++;
                    continue;
                }

                executedSells++;
                distributionState.OnExit(simLatestBuyPrice.Value, price);
                simLatestSellPrice = price;
                lastAcceptedSellSignalPrice = price;
                lastAcceptedBuySignalPrice = null;
                simLatestBuyPrice = null;
                simHasOpenPosition = false;
                simTrailingHighWaterMark = 0m;
                lastSellTimestamp = timestamp;

                if (advice.Contains("FireSell"))
                {
                    simLowestBuyAfterFireSell = null;
                    simHighestPanicBuyAfterLowest = null;
                }
            }
        }

        var persistedBuyLogsInRange = await tradingRepository.CountBuyActionLogsInRange(startDate, endDate, cancellationToken);
        var persistedSellLogsInRange = await tradingRepository.CountSellActionLogsInRange(startDate, endDate, cancellationToken);

        return new BacktestDecisionDiagnostics
        {
            StartDate = startDate,
            EndDate = endDate,
            TimestampsEvaluated = timestamps.Count,
            RawBuySignals = rawBuySignals,
            RawSellSignals = rawSellSignals,
            RawWaitSignals = rawWaitSignals,
            PostRegimeBuySignals = postRegimeBuySignals,
            PostRegimeSellSignals = postRegimeSellSignals,
            PostRegimeWaitSignals = postRegimeWaitSignals,
            RegimeBullSamples = regimeBullSamples,
            RegimeBearSamples = regimeBearSamples,
            RegimeNeutralSamples = regimeNeutralSamples,
            ExecutedBuys = executedBuys,
            ExecutedSells = executedSells,
            HardStopLossTriggers = hardStopLossTriggers,
            BlockedBuyDueToOpenPosition = blockedBuyDueToOpenPosition,
            BlockedBuyDueToFollowThrough = blockedBuyDueToFollowThrough,
            BlockedBuyDueToBearMarket = blockedBuyDueToBearMarket,
            BlockedBuyDueToCooldown = blockedBuyDueToCooldown,
            BlockedSellDueToNoOpenPosition = blockedSellDueToNoOpenPosition,
            BlockedSellDueToFollowThrough = blockedSellDueToFollowThrough,
            PersistedBuyLogsInRange = persistedBuyLogsInRange,
            PersistedSellLogsInRange = persistedSellLogsInRange,
            DecisionSourceCounts = decisionSourceCounts
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key.ToString(), StringComparer.Ordinal)
                .Select(x => new BacktestDiagnosticsCountRow(x.Key.ToString(), x.Value))
                .ToList(),
            AlignedRangesRawCounts = alignedRangesRawCounts
                .OrderBy(x => x.Key)
                .Select(x => new BacktestDiagnosticsCountRow(x.Key.ToString(), x.Value))
                .ToList(),
            TrendCountSumRawCounts = trendCountRawCounts
                .OrderBy(x => x.Key)
                .Select(x => new BacktestDiagnosticsCountRow(x.Key.ToString(), x.Value))
                .ToList(),
            BullishTupleRawCounts = bullishTupleRawCounts
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new BacktestDiagnosticsCountRow(x.Key, x.Value))
                .ToList(),
            BearishTupleRawCounts = bearishTupleRawCounts
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new BacktestDiagnosticsCountRow(x.Key, x.Value))
                .ToList()
        };
    }

    private async Task<BacktestSummary> RunBacktestCoreAsync(
        IReadOnlyList<DateTime> timestamps,
        IReadOnlyList<StockCandle> mstrCandles,
        DateTime startDate,
        DateTime endDate,
        bool persist,
        double momentumBrakeMaxRatio,
        double troughMinDrawdownPercent,
        double troughRecoveryProximityPercent,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet,
        double bearishRegimeMinLinearBullishScore,
        CancellationToken cancellationToken,
        int reEntryCooldownHoursOverride = -1,
        int smaExitDebounceDaysOverride = -1,
        bool bullishRegimeBlockBuy = false,
        bool entryRequiresPriceAboveSma200 = false,
        double breakevenTriggerPercent = 0.0,
        double trailingStopPercentOverride = 0.0,
        double trailingStopActivationPercentOverride = 0.0,
        double nearAthSellSuppressMinPercentOverride = 0.0,
        int athProximityBuyBlockMaxAthAgeDaysOverride = 0,
        bool macroOnlyMode = false)
    {
        // If caller provides an explicit override (≥ 0), use it; otherwise fall back to appsettings.
        var effectiveCooldownHours = reEntryCooldownHoursOverride >= 0
            ? reEntryCooldownHoursOverride
            : riskControlOptions.Value.ReEntryCooldownHours;

        var effectiveSmaExitDebounceDays = smaExitDebounceDaysOverride >= 0
            ? smaExitDebounceDaysOverride
            : riskControlOptions.Value.SmaExitDebounceDays;

        var effectiveTrailingStopPercent = trailingStopPercentOverride > 0
            ? trailingStopPercentOverride
            : riskControlOptions.Value.TrailingStopPercent;
        var effectiveTrailingStopActivationPercent = trailingStopActivationPercentOverride > 0
            ? trailingStopActivationPercentOverride
            : riskControlOptions.Value.TrailingStopActivationPercent;

        // nearAthSellSuppressMinPercent override: 0 = use appsettings, >100 = disable suppression entirely
        var effectiveNearAthSuppressEnabled = nearAthSellSuppressMinPercentOverride == 0.0
            ? bearMarketFilterOptions.Value.NearAthSellSuppressEnabled
            : nearAthSellSuppressMinPercentOverride <= 100.0;
        var effectiveNearAthSuppressMinPercent = nearAthSellSuppressMinPercentOverride == 0.0
            ? bearMarketFilterOptions.Value.NearAthSellSuppressMinPercent
            : nearAthSellSuppressMinPercentOverride;

        // athProximityBuyBlockMaxAthAgeDays override: 0 = appsettings, >0 = override days, -1 = disable entire gate
        var effectiveAthProximityBuyBlockEnabled = athProximityBuyBlockMaxAthAgeDaysOverride == -1
            ? false
            : bearMarketFilterOptions.Value.AthProximityBuyBlockEnabled;
        var effectiveAthProximityMaxAthAgeDays = athProximityBuyBlockMaxAthAgeDaysOverride > 0
            ? athProximityBuyBlockMaxAthAgeDaysOverride
            : bearMarketFilterOptions.Value.AthProximityBuyBlockMaxAthAgeDays;

        // In-Memory-Simulationszustand (entspricht dem, was bei /rerun aus der DB gelesen würde)
        decimal? simLatestBuyPrice = null;
        decimal? simLatestSellPrice = null;
        decimal? lastAcceptedBuySignalPrice = null;
        decimal? lastAcceptedSellSignalPrice = null;
        BuyActionLog? simLowestBuyAfterFireSell = null;
        BuyActionLog? simHighestPanicBuyAfterLowest = null;
        var simHasOpenPosition = false;
        DateTime? lastSellTimestamp = null;
        DateTime? lastCycleTopForcedSellAt = null;
        BuyActionLog? lastPersistedBuy = null;

        // Postmortem state per open position (reset on entry/exit)
        DateTime? entryTimestamp = null;
        decimal runMaxPrice = 0m;
        decimal runMinPrice = 0m;
        int? entryAlignedRanges = null;
        int? entryTrendCountSum = null;
        bool breakevenActive = false;
        decimal simTrailingHighWaterMark = 0m;

        // Phase 2: Deduplicate before persist (avoid duplicates on retry)
        if (persist)
        {
            await tradingRepository.DeleteSellBuyActionLogsInRange(startDate, endDate, cancellationToken);
        }

        // Distribution-top tracking state (concepts A + B)
        var distributionState = new DistributionTopState();

        // Pre-fetch 2h candle map for realistic intra-bar stop-loss evaluation (uses bar low).
        var candleMap = await BuildCandleMapAsync(endDate, cancellationToken);

        // Pre-compute daily SMA200 "below" lookup for SMA-hold exit mode and/or entry gate.
        var dailyBelowSma200 = (effectiveSmaExitDebounceDays > 0 || entryRequiresPriceAboveSma200)
            ? await BuildDailyBelowSma200LookupAsync(startDate, endDate, cancellationToken)
            : new Dictionary<DateOnly, bool>();

        // Pre-compute daily price/SMA(730d) ratio lookup for the post-cycle-top SMA-ratio buy block.
        var dailySmaRatio730 = (bearMarketFilterOptions.Value.PostCycleTopBuyBlockEnabled
                && bearMarketFilterOptions.Value.PostCycleTopSmaRatioBlockEnabled)
            ? await BuildDailySmaRatioLookupAsync(
                startDate, endDate,
                bearMarketFilterOptions.Value.PostCycleTopSmaRatioPeriodDays,
                cancellationToken)
            : new Dictionary<DateOnly, double>();

        // Phase 3: Pre-fetch separate candle sets for buy-override (AthDrawdownInterval, typically 1h)
        // and sell-suppression (AthDrawdownSuppressInterval, typically 1d)
        var athDrawdownCandlesForBuy = (bearMarketFilterOptions.Value.AthDrawdownOverrideEnabled || macroOnlyMode)
            ? await binanceKlineService.GetCandlesUpToAsync(
                bearMarketFilterOptions.Value.AthDrawdownInterval,
                endDate.ToUniversalTime(),
                cancellationToken)
            : [];
        var athDrawdownCandlesForSellSuppress = (bearMarketFilterOptions.Value.AthDrawdownSuppressBearishSellEnabled)
            ? await binanceKlineService.GetCandlesUpToAsync(
                bearMarketFilterOptions.Value.AthDrawdownSuppressInterval,
                endDate.ToUniversalTime(),
                cancellationToken)
            : [];

        // CycleTop: Einmal vorab berechnen statt pro Timestamp (vermeidet 40k List-Allokationen und Timeouts)
        var (cycleTopMap, _) = (bearMarketFilterOptions.Value.CycleTopSellZoneEnabled || macroOnlyMode)
            ? await PrecomputeCycleTopMapAsync(endDate, timestamps, cancellationToken)
            : ((IReadOnlyDictionary<DateOnly, bool>)new Dictionary<DateOnly, bool>(),
               (IReadOnlyDictionary<DateOnly, double>)new Dictionary<DateOnly, double>());

        // PiCycleTop: Einmal vorab berechnen
        var piCycleTopMap = (bearMarketFilterOptions.Value.PiCycleTopSellEnabled || macroOnlyMode)
            ? await PrecomputePiCycleTopMapAsync(endDate, timestamps, cancellationToken)
            : (IReadOnlyDictionary<DateOnly, bool>)new Dictionary<DateOnly, bool>();

        // PuellMultiple: Einmal vorab berechnen
        var puellMultipleMap = bearMarketFilterOptions.Value.PuellMultipleBuyBoostEnabled
            ? await PrecomputePuellMultipleMapAsync(endDate, timestamps, cancellationToken)
            : (IReadOnlyDictionary<DateOnly, bool>)new Dictionary<DateOnly, bool>();

        // Phase 4: Pre-compute bear-market and deep-cycle-low maps to avoid per-timestamp
        // async service calls (lock acquisitions + O(n²) LINQ sorts inside IsBearMarketAsync /
        // IsDeepCycleLowAsync). The candle lists were already fetched above; only O(n) work here.
        var bearMarketDailyCandles = bearMarketFilterOptions.Value.Enabled
            ? await binanceKlineService.GetCandlesUpToAsync(
                bearMarketFilterOptions.Value.Interval,
                endDate.ToUniversalTime(),
                cancellationToken)
            : (IReadOnlyList<OhlcvCandle>)[];
        var bearMarketMap = bearMarketFilterOptions.Value.Enabled
            ? BuildBearMarketMap(bearMarketDailyCandles, bearMarketFilterOptions.Value.MovingAveragePeriod, timestamps)
            : (IReadOnlyDictionary<DateOnly, bool>)new Dictionary<DateOnly, bool>();
        var deepCycleLowBuyMap = (bearMarketFilterOptions.Value.AthDrawdownOverrideEnabled || macroOnlyMode)
            ? BuildDeepCycleLowMap(
                athDrawdownCandlesForBuy, timestamps,
                t => AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(bearMarketFilterOptions.Value, t))
            : (IReadOnlyDictionary<DateOnly, bool>)new Dictionary<DateOnly, bool>();
        var deepCycleLowSellMap = bearMarketFilterOptions.Value.AthDrawdownSuppressBearishSellEnabled
            ? BuildDeepCycleLowMap(
                athDrawdownCandlesForSellSuppress, timestamps,
                t => AthDrawdownThresholdPolicy.ResolveSuppressThresholdPercent(bearMarketFilterOptions.Value, t))
            : (IReadOnlyDictionary<DateOnly, bool>)new Dictionary<DateOnly, bool>();

        // ATH-Proximity buy-block map: maps each 2h timestamp to the running ATH (max High of all
        // preceding 2h candles). In the buy section we compare the ACTUAL signal price (result.CurrentPrice)
        // against this ATH — not the 2h candle close — because on volatile days the two can diverge.
        var needAthRunningMap = effectiveAthProximityBuyBlockEnabled
            || (bearMarketFilterOptions.Value.PostCycleTopBuyBlockEnabled
                && bearMarketFilterOptions.Value.PostCycleTopAthDrawdownBlockEnabled);
        var athProximityBuyBlockCandles2h = needAthRunningMap
            ? await binanceKlineService.GetCandlesUpToAsync("2h", endDate.ToUniversalTime(), cancellationToken)
            : (IReadOnlyList<OhlcvCandle>)[];
        var athRunningAthMap = needAthRunningMap
            ? BuildRunningAthMap(athProximityBuyBlockCandles2h, timestamps)
            : (IReadOnlyDictionary<DateTime, (double Ath, DateTime AthSetAt)>)new Dictionary<DateTime, (double, DateTime)>();

        // Near-ATH sell-suppression map: running ATH on the configured daily interval.
        // Separate from the 2h buy-block map so intervals can differ. Only built when enabled.
        var nearAthSellSuppressInterval = bearMarketFilterOptions.Value.NearAthSellSuppressInterval;
        var nearAthSellSuppressCandles = effectiveNearAthSuppressEnabled
            ? await binanceKlineService.GetCandlesUpToAsync(nearAthSellSuppressInterval, endDate.ToUniversalTime(), cancellationToken)
            : (IReadOnlyList<OhlcvCandle>)[];
        var nearAthSellSuppressMap = effectiveNearAthSuppressEnabled
            ? BuildRunningAthMap(nearAthSellSuppressCandles, timestamps)
            : (IReadOnlyDictionary<DateTime, (double Ath, DateTime AthSetAt)>)new Dictionary<DateTime, (double, DateTime)>();

        decimal? firstPrice = null;
        decimal? lastPrice = null;
        var trades = new List<BacktestTrade>();

        // Risk-adjusted metric tracking (ADR-016: drawdown comparison vs HODL)
        var lastClosedEquity = 1.0;   // strategy equity after last closed trade
        var strategyEquity = 1.0;     // running strategy equity (1.0 = initial capital)
        var strategyEquityPeak = 1.0;
        var strategyMaxDrawdown = 0.0;
        var hodlPeak = 0.0;           // running max price for HODL drawdown
        var hodlMaxDrawdown = 0.0;

        foreach (var timestamp in timestamps)
        {
            var (strategyHigh, strategyLow) = LookupMicrostrategyPrice(mstrCandles, timestamp);

            IReadOnlyCollection<BitcoinTradeStatus> results;

            // macroOnlyMode: skip the trade advisor entirely; derive price from the 2h candle map
            // and synthesise a minimal BitcoinTradeStatus with advice=Wait (the macro override
            // block below will replace it). This avoids thousands of indicator API calls.
            if (macroOnlyMode)
            {
                decimal macroPrice = 0m;
                if (candleMap.TryGetValue(timestamp, out var macroCandle) && macroCandle.Close > 0)
                    macroPrice = (decimal)macroCandle.Close;
                if (macroPrice == 0) continue;
                results = new[] { new BitcoinTradeStatus { TradeAdvice = "Wait", CurrentPrice = (double)macroPrice } };
            }
            else
            {
            try
            {
                results = await GetBitcoinTradeAdviceAsync(
                    timestamp,
                    simLatestBuyPrice,
                    simLatestSellPrice,
                    strategyHigh,
                    strategyLow,
                    simLowestBuyAfterFireSell,
                    simHighestPanicBuyAfterLowest,
                    confluenceThresholdSet,
                    cancellationToken);
            }
            catch (InvalidDataException)
            {
                // Early timestamps can lack enough lookback candles for all indicators.
                // Skip until warm-up is sufficient instead of aborting the full backtest.
                continue;
            }
            } // end !macroOnlyMode advisor block

            if (results.Count == 0)
            {
                continue;
            }

            var result = results.First();
            var price = (decimal)result.CurrentPrice;
            if (price == 0)
            {
                continue;
            }

            firstPrice ??= price;
            lastPrice = price;

            // Update HODL and strategy equity curves for max-drawdown tracking
            var priceD = (double)price;
            if (priceD > hodlPeak) hodlPeak = priceD;
            if (hodlPeak > 0)
                hodlMaxDrawdown = Math.Max(hodlMaxDrawdown, (hodlPeak - priceD) / hodlPeak * 100.0);
            strategyEquity = simHasOpenPosition && simLatestBuyPrice.HasValue && simLatestBuyPrice.Value > 0m
                ? lastClosedEquity * priceD / (double)simLatestBuyPrice.Value
                : lastClosedEquity;
            if (strategyEquity > strategyEquityPeak) strategyEquityPeak = strategyEquity;
            if (strategyEquityPeak > 0)
                strategyMaxDrawdown = Math.Max(strategyMaxDrawdown, (strategyEquityPeak - strategyEquity) / strategyEquityPeak * 100.0);

            // Tick distribution state on every price update when position is open
            if (simHasOpenPosition)
                distributionState.Tick(price);

            // Postmortem: track MFE/MAE using bar high/low when available, else close price.
            if (simHasOpenPosition && entryTimestamp.HasValue && candleMap.TryGetValue(timestamp, out var bar))
            {
                var hi = (decimal)bar.High;
                var lo = (decimal)bar.Low;
                if (hi > runMaxPrice) runMaxPrice = hi;
                if (lo < runMinPrice || runMinPrice == 0m) runMinPrice = lo;
                if (hi > simTrailingHighWaterMark) simTrailingHighWaterMark = hi;
            }
            else if (simHasOpenPosition && entryTimestamp.HasValue)
            {
                if (price > runMaxPrice) runMaxPrice = price;
                if (price < runMinPrice || runMinPrice == 0m) runMinPrice = price;
                if (price > simTrailingHighWaterMark) simTrailingHighWaterMark = price;
            }

            // Breakeven-stop activation: once the CLOSE price (not bar.High) exceeds the trigger
            // threshold from entry. Using close price prevents whipsaw exits caused by brief
            // intraday spikes that activate breakeven and then retrace back to entry.
            if (simHasOpenPosition && simLatestBuyPrice.HasValue && breakevenTriggerPercent > 0 && !breakevenActive)
            {
                var closePricePct = (double)(price / simLatestBuyPrice.Value - 1m) * 100.0;
                if (closePricePct >= breakevenTriggerPercent)
                    breakevenActive = true;
            }

            decimal? barLow = candleMap.TryGetValue(timestamp, out var barCandle) ? (decimal)barCandle.Low : null;

            var breakevenStopEval = new StopLossEvaluation(false, 0m);
            if (simLatestBuyPrice.HasValue && breakevenActive)
            {
                // Threshold 0% → stop fires when price returns to entry level.
                breakevenStopEval = riskControlService.EvaluateHardStop(
                    simLatestBuyPrice.Value, price, barLow,
                    thresholdPercent: 0.0,
                    riskControlOptions.Value.StopLossSlippagePercent);
            }

            var atrStopEval = new StopLossEvaluation(false, 0m);
            if (!macroOnlyMode && simLatestBuyPrice.HasValue && riskControlOptions.Value.EnableAtrStopLoss)
            {
                var atrValue = await atrLookupService.GetAtrAsync(
                    riskControlOptions.Value.AtrInterval,
                    timestamp,
                    riskControlOptions.Value.AtrPeriod,
                    cancellationToken);
                atrStopEval = riskControlService.EvaluateAtrStop(
                    simLatestBuyPrice.Value,
                    price,
                    barLow,
                    atrValue,
                    riskControlOptions.Value.AtrMultiplier,
                    riskControlOptions.Value.StopLossSlippagePercent);
            }

            var hardStopEval = new StopLossEvaluation(false, 0m);
            if (!macroOnlyMode && simLatestBuyPrice.HasValue && riskControlOptions.Value.EnableHardStopLoss)
            {
                hardStopEval = riskControlService.EvaluateHardStop(
                    simLatestBuyPrice.Value,
                    price,
                    barLow,
                    riskControlOptions.Value.HardStopLossPercent,
                    riskControlOptions.Value.StopLossSlippagePercent);
            }

            var trailStopEval = new StopLossEvaluation(false, 0m);
            if (!macroOnlyMode && simLatestBuyPrice.HasValue && riskControlOptions.Value.EnableTrailingStop && simTrailingHighWaterMark > 0m)
            {
                trailStopEval = riskControlService.EvaluateTrailingStop(
                    simLatestBuyPrice.Value,
                    price,
                    simTrailingHighWaterMark,
                    effectiveTrailingStopActivationPercent,
                    effectiveTrailingStopPercent,
                    riskControlOptions.Value.StopLossSlippagePercent);
            }

            if (simLatestBuyPrice.HasValue && (atrStopEval.Triggered || hardStopEval.Triggered || breakevenStopEval.Triggered || trailStopEval.Triggered))
            {
                // When multiple stops fire, take the highest exit price (least loss).
                var candidates = new[] { atrStopEval, hardStopEval, breakevenStopEval, trailStopEval }
                    .Where(e => e.Triggered).Select(e => e.ExitPrice);
                var exitPrice = candidates.Max();
                var exitAdvice = trailStopEval.Triggered && !hardStopEval.Triggered && !atrStopEval.Triggered && !breakevenStopEval.Triggered
                    ? "TrailingStop"
                    : breakevenStopEval.Triggered && exitPrice >= breakevenStopEval.ExitPrice
                        && !hardStopEval.Triggered && !atrStopEval.Triggered
                    ? "BreakevenStop" : "HardStopLoss";

                var stopLossProfit = PercentageService.GetPercentage(
                    (double)(exitPrice - simLatestBuyPrice.Value),
                    (double)simLatestBuyPrice.Value);

                trades.Add(new BacktestTrade
                {
                    Timestamp = timestamp,
                    TradeAdvice = exitAdvice,
                    Price = exitPrice,
                    ProfitPercent = stopLossProfit,
                    EntryTimestamp = entryTimestamp,
                    EntryPrice = simLatestBuyPrice,
                    HoldHours = entryTimestamp.HasValue ? (timestamp - entryTimestamp.Value).TotalHours : null,
                    MaxFavorableExcursionPercent = runMaxPrice > 0m
                        ? PercentageService.GetPercentage((double)(runMaxPrice - simLatestBuyPrice.Value), (double)simLatestBuyPrice.Value)
                        : null,
                    MaxAdverseExcursionPercent = runMinPrice > 0m
                        ? PercentageService.GetPercentage((double)(runMinPrice - simLatestBuyPrice.Value), (double)simLatestBuyPrice.Value)
                        : null,
                    EntryAlignedRanges = entryAlignedRanges,
                    EntryTrendCountSum = entryTrendCountSum
                });

                if (persist && lastPersistedBuy != null)
                {
                    var stopLossLog = new SellActionLog
                    {
                        Price = exitPrice,
                        TradingDate = timestamp,
                        TradeAdvice = exitAdvice,
                        BuyActionLog = lastPersistedBuy,
                        IsValidated = true,
                        AlignedRanges = result.AlignedRanges,
                        TrendCountSum = result.TrendCountSum
                    };
                    await tradingRepository.SaveSellActionLog(stopLossLog, cancellationToken);
                    lastPersistedBuy = null;
                }

                distributionState.OnExit(simLatestBuyPrice.Value, exitPrice);
                lastClosedEquity = lastClosedEquity * (double)exitPrice / (double)simLatestBuyPrice.Value;
                simLatestSellPrice = exitPrice;
                lastAcceptedSellSignalPrice = exitPrice;
                lastAcceptedBuySignalPrice = null;
                simLatestBuyPrice = null;
                simHasOpenPosition = false;
                simLowestBuyAfterFireSell = null;
                simHighestPanicBuyAfterLowest = null;
                lastSellTimestamp = timestamp;
                entryTimestamp = null;
                runMaxPrice = 0m;
                runMinPrice = 0m;
                entryAlignedRanges = null;
                entryTrendCountSum = null;
                breakevenActive = false;
                simTrailingHighWaterMark = 0m;
                continue;
            }

            // Distribution-top sell check: override advice when either concept A or B fires
            var advice = result.TradeAdvice;

            // macroOnlyMode: bypass all micro-signals; only DeepCycleLow (buy) and
            // CycleTopZone (sell) determine the advice. Pi Cycle Top excluded (fires too
            // early). Everything else is Wait. All buy gates are bypassed for macroOnly
            // because the DeepCycleLow signal already enforces cycle-timing constraints.
            if (macroOnlyMode)
            {
                var dateKey = DateOnly.FromDateTime(timestamp.ToUniversalTime().Date);
                var isDeepLow  = deepCycleLowBuyMap.TryGetValue(dateKey, out var dl) && dl;
                var isCycleTop = cycleTopMap.TryGetValue(dateKey, out var ct)        && ct;

                advice = isCycleTop && simHasOpenPosition  ? "DistributionSell"
                       : isDeepLow  && !simHasOpenPosition ? "StrongBuy"
                                                           : "Wait";
                result.TradeAdvice = advice;
            }
            // macroOnlyMode: treat macro-driven DistributionSell as a cycle-top forced sell so
            // it bypasses the near-ATH sell suppression gate below.
            var cycleTopForcedSell = macroOnlyMode && advice == "DistributionSell";
            if (!macroOnlyMode && simHasOpenPosition
                && simLatestBuyPrice.HasValue
                && !advice.Contains("Sell")
                && DistributionTopFilter.ShouldTriggerDistributionSell(simLatestBuyPrice.Value, price, distributionState,
                    troughMinDrawdownPercent: troughMinDrawdownPercent,
                    momentumBrakeMaxRatio: momentumBrakeMaxRatio,
                    troughRecoveryProximityPercent: troughRecoveryProximityPercent))
            {
                advice = "DistributionSell";
                result.DecisionSource = DecisionSource.DistributionTop;
            }

            result.TradeAdvice = advice;
            var regime = (await marketRegimeService.GetRegimeAsync(timestamp, cancellationToken)).Regime;
            if (!macroOnlyMode)
                advice = await ApplyRegimeWithBonusAsync(result, regime, bearishRegimeMinLinearBullishScore, timestamp, cancellationToken, bullishRegimeBlockBuy);
            if (!macroOnlyMode && ShouldForceCycleTopSellFromMap(advice, timestamp, simHasOpenPosition, cycleTopMap))
            {
                advice = "DistributionSell";
                result.TradeAdvice = advice;
                result.DecisionSource = DecisionSource.DistributionTop;
                cycleTopForcedSell = true;
            }
            if (!macroOnlyMode && ShouldForceCycleTopSellFromMap(advice, timestamp, simHasOpenPosition, piCycleTopMap))
            {
                advice = "DistributionSell";
                result.TradeAdvice = advice;
                result.DecisionSource = DecisionSource.DistributionTop;
                cycleTopForcedSell = true;
            }

            if (advice.Contains("Buy"))
            {
                if (simHasOpenPosition)
                {
                    continue;
                }

                var deepCycleOverrideTriggered = false;

                var passesBullish = SignalFollowThroughFilter.PassesBullish(
                    price,
                    lastAcceptedBuySignalPrice,
                    SignalFollowThroughThresholds.BullishMinFollowThroughPercent);

                if (!passesBullish)
                {
                    continue;
                }

                if (!macroOnlyMode
                    && bearMarketFilterOptions.Value.Enabled
                    && bearMarketMap.TryGetValue(DateOnly.FromDateTime(timestamp.ToUniversalTime().Date), out var isBearMkt) && isBearMkt
                    && !(deepCycleOverrideTriggered = (bearMarketFilterOptions.Value.AthDrawdownOverrideEnabled
                         && deepCycleLowBuyMap.TryGetValue(DateOnly.FromDateTime(timestamp.ToUniversalTime().Date), out var isDeepBuyLow) && isDeepBuyLow))
                    && !(puellMultipleMap.TryGetValue(DateOnly.FromDateTime(timestamp.ToUniversalTime().Date), out var isPuellUndervalued) && isPuellUndervalued))
                {
                    continue;
                }

                var executionPrice = price;
                var executionTimestamp = timestamp;
                if (deepCycleOverrideTriggered
                    && !TryResolveNextCandleOpen(
                           athDrawdownCandlesForBuy,
                        timestamp,
                        out executionTimestamp,
                        out executionPrice))
                {
                    // If there is no next candle in the requested range, skip execution.
                    continue;
                }

                var cooldownHours = effectiveCooldownHours;
                if (cooldownHours > 0
                    && lastSellTimestamp.HasValue
                    && (timestamp - lastSellTimestamp.Value).TotalHours < cooldownHours)
                {
                    continue;
                }

                if (entryRequiresPriceAboveSma200)
                {
                    var entryDate = DateOnly.FromDateTime(timestamp.ToUniversalTime());
                    if (dailyBelowSma200.TryGetValue(entryDate, out var belowSma) && belowSma)
                        continue;
                }

                // Post-cycle-top buy block: suppress re-entries for N days after a forced
                // cycle-top sell (PiCycleTop / CycleTopZone) to avoid whipsaw re-entries.
                // macroOnly bypasses this gate — DeepCycleLow buys are macro timing signals
                // that should not be suppressed by a post-sell day-count block.
                if (bearMarketFilterOptions.Value.PostCycleTopBuyBlockEnabled
                    && lastCycleTopForcedSellAt.HasValue
                    && (timestamp - lastCycleTopForcedSellAt.Value).TotalDays < bearMarketFilterOptions.Value.PostCycleTopBuyBlockDays
                    && !macroOnlyMode)
                {
                    continue;
                }

                // Post-cycle-top SMA-ratio gate: after a DistributionSell, additionally block
                // re-entry while price > MaxMultiplier × SMA(730d). Inspired by the 2yr MA
                // Multiplier chart — a ratio ≥ 1.5 historically indicates macro overvaluation.
                // No time-expiry: the gate clears automatically once price cools toward the SMA.
                // macroOnly bypasses this gate — DeepCycleLow buys are by definition deep below
                // the 2yr SMA, so the ratio condition would never block them anyway.
                if (bearMarketFilterOptions.Value.PostCycleTopBuyBlockEnabled
                    && bearMarketFilterOptions.Value.PostCycleTopSmaRatioBlockEnabled
                    && lastCycleTopForcedSellAt.HasValue
                    && !macroOnlyMode)
                {
                    var ratioDate = DateOnly.FromDateTime(timestamp.ToUniversalTime().Date);
                    if (dailySmaRatio730.TryGetValue(ratioDate, out var smaRatio)
                        && smaRatio > bearMarketFilterOptions.Value.PostCycleTopSmaRatioMaxMultiplier)
                    {
                        continue;
                    }
                }

                // Post-cycle-top ATH-drawdown gate: after a DistributionSell, block re-entry
                // while price is still above MaxAthProximityPercent % of the running ATH.
                // Distinguishes bear-market bounces (40-65 % of ATH) from deep-cycle-low
                // accumulation zones (< 50 % of ATH). No time-expiry.
                // macroOnly bypasses this gate — DeepCycleLow buys are deep below ATH by
                // definition (≤ 50 % of ATH), so the proximity check is always satisfied.
                if (bearMarketFilterOptions.Value.PostCycleTopBuyBlockEnabled
                    && bearMarketFilterOptions.Value.PostCycleTopAthDrawdownBlockEnabled
                    && lastCycleTopForcedSellAt.HasValue
                    && athRunningAthMap.TryGetValue(timestamp, out var postCycleAthState)
                    && postCycleAthState.Ath > 0
                    && (double)price / postCycleAthState.Ath * 100.0
                        > bearMarketFilterOptions.Value.PostCycleTopAthDrawdownMaxAthProximityPercent
                    && !macroOnlyMode)
                {
                    continue;
                }

                // Block new entry while Pi Cycle Top or Cycle Top zone is active — entering
                // would immediately trigger a DistributionSell (tight-loop guard, ADR-008).
                // macroOnly bypasses both guards: the macroOnly block already guarantees no
                // DeepCycleLow fires while isCycleTop is active (they are mutually exclusive
                // market conditions), so this gate is redundant for macroOnly.
                var cycleTopBuyCheckDate = DateOnly.FromDateTime(timestamp.ToUniversalTime().Date);
                if (piCycleTopMap.TryGetValue(cycleTopBuyCheckDate, out var piTopIsActive) && piTopIsActive
                    && !macroOnlyMode)
                    continue;
                if (cycleTopMap.TryGetValue(cycleTopBuyCheckDate, out var cycleTopIsActive) && cycleTopIsActive
                    && !macroOnlyMode)
                    continue;

                // ATH-Proximity gate (ADR-009): block new buy when the ACTUAL buy price is too near the
                // running all-time high AND the ATH is recent (within AthProximityBuyBlockMaxAthAgeDays).
                // Near-ATH buys far from the ATH date indicate pre-breakout accumulation (allowed).
                // Near-ATH buys shortly after the ATH date indicate cycle-top entries (blocked).
                // macroOnly bypasses this gate — DeepCycleLow buys are deep below ATH by definition.
                if (effectiveAthProximityBuyBlockEnabled
                    && !macroOnlyMode
                    && athRunningAthMap.TryGetValue(timestamp, out var athState)
                    && athState.Ath > 0
                    && (double)price / athState.Ath * 100.0 > bearMarketFilterOptions.Value.AthProximityBuyBlockMaxPercent)
                {
                    var maxAthAgeDays = effectiveAthProximityMaxAthAgeDays;
                    var athAgeDays = (timestamp - athState.AthSetAt).TotalDays;
                    if (maxAthAgeDays <= 0 || athAgeDays <= maxAthAgeDays)
                        continue;
                }

                // Persist only after ALL entry gates pass — only actually executed trades are saved.
                if (persist)
                {
                    var buyLog = new BuyActionLog
                    {
                        Price = executionPrice,
                        TradingDate = executionTimestamp,
                        TradeAdvice = advice,
                        IsValidated = true,
                        AlignedRanges = result.AlignedRanges,
                        TrendCountSum = result.TrendCountSum
                    };
                    await tradingRepository.SaveBuyActionLog(buyLog, cancellationToken);
                    lastPersistedBuy = buyLog;
                }

                trades.Add(new BacktestTrade
                {
                    Timestamp = executionTimestamp,
                    TradeAdvice = advice,
                    Price = executionPrice
                });

                simLatestBuyPrice = executionPrice;
                lastAcceptedBuySignalPrice = executionPrice;
                lastAcceptedSellSignalPrice = null;
                simHasOpenPosition = true;
                entryTimestamp = executionTimestamp;
                runMaxPrice = executionPrice;
                runMinPrice = executionPrice;
                entryAlignedRanges = result.AlignedRanges;
                entryTrendCountSum = result.TrendCountSum;
                breakevenActive = false;
                simTrailingHighWaterMark = executionPrice;

                distributionState.OnEntry(executionPrice);

                // A successful re-entry clears the post-cycle-top block so that subsequent
                // sell + re-entry cycles after the bear market are not permanently gated.
                lastCycleTopForcedSellAt = null;

                // Niedrigsten Kauf nach dem letzten FireSell aktualisieren
                if (simLowestBuyAfterFireSell == null || executionPrice < simLowestBuyAfterFireSell.Price)
                {
                    // Neuer niedrigster Kauf → PanicBuy-Tracking zurücksetzen
                    simLowestBuyAfterFireSell = new BuyActionLog { Price = executionPrice, TradingDate = executionTimestamp, TradeAdvice = advice };
                    simHighestPanicBuyAfterLowest = null;
                }

                // Höchsten PanicBuy nach dem niedrigsten Kauf aktualisieren
                if (advice.Contains("Panic") && simLowestBuyAfterFireSell != null
                    && executionTimestamp > simLowestBuyAfterFireSell.TradingDate)
                {
                    if (simHighestPanicBuyAfterLowest == null || executionPrice > simHighestPanicBuyAfterLowest.Price)
                    {
                        simHighestPanicBuyAfterLowest = new BuyActionLog { Price = executionPrice, TradingDate = executionTimestamp, TradeAdvice = advice };
                    }
                }
            }
            else if (advice.Contains("Sell"))
            {
                if (!simHasOpenPosition || !simLatestBuyPrice.HasValue)
                {
                    continue;
                }

                // Minimum hold: suppress regular Sell/DistributionSell signals if the position
                // has not been held for the minimum required duration. FireSell, HardStopLoss,
                // TrailingStop, and BreakevenStop (handled in the stop block above) bypass this.
                if (riskControlOptions.Value.MinHoldHours > 0
                    && !advice.Contains("FireSell")
                    && !advice.Contains("HardStop")
                    && !advice.Contains("TrailingStop")
                    && entryTimestamp.HasValue
                    && (timestamp - entryTimestamp.Value).TotalHours < riskControlOptions.Value.MinHoldHours)
                {
                    continue;
                }

                // SMA-hold exit: suppress regular Sell/DistributionSell signals while BTC is above
                // 200d-SMA (or has not been below it for effectiveSmaExitDebounceDays consecutive days).
                // HardStopLoss, FireSell, and CycleTop forced sells bypass this gate — cycle-top
                // exits are macro-timing signals that fire at bull-market peaks, which are always
                // well above the 200d-SMA, so the SMA filter would incorrectly suppress them.
                if (effectiveSmaExitDebounceDays > 0
                    && !advice.Contains("FireSell")
                    && !cycleTopForcedSell)
                {
                    var checkDate = DateOnly.FromDateTime(timestamp);
                    var belowLongEnough = true;
                    for (var offset = 0; offset < effectiveSmaExitDebounceDays; offset++)
                    {
                        if (!dailyBelowSma200.TryGetValue(checkDate.AddDays(-offset), out var below) || !below)
                        {
                            belowLongEnough = false;
                            break;
                        }
                    }
                    if (!belowLongEnough)
                        continue; // stay in position: BTC still above SMA or not yet N days below
                }

                // Deep-cycle-low sell suppression: regular bearish exits are held in the zone.
                // Emergency exits (FireSell, HardStopLoss, TrailingStop) bypass this gate.
                if (bearMarketFilterOptions.Value.AthDrawdownSuppressBearishSellEnabled
                    && !advice.Contains("FireSell")
                    && !advice.Contains("HardStop")
                    && !advice.Contains("TrailingStop")
                    && deepCycleLowSellMap.TryGetValue(DateOnly.FromDateTime(timestamp.ToUniversalTime().Date), out var isDeepSellLow) && isDeepSellLow)
                {
                    continue;
                }

                // Near-ATH sell suppression: when price is still near the ATH the bull run is
                // likely ongoing. Regular Sell/DistributionSell are suppressed; only emergency
                // exits (FireSell, HardStopLoss, TrailingStop) can close the position.
                // Cycle-top forced sells (PiCycleTop, CycleTopZone) bypass this gate —
                // these are macro-timing signals specifically designed to exit at the top.
                if (effectiveNearAthSuppressEnabled
                    && !advice.Contains("FireSell")
                    && !advice.Contains("HardStop")
                    && !advice.Contains("TrailingStop")
                    && !cycleTopForcedSell
                    && nearAthSellSuppressMap.TryGetValue(timestamp, out var nearAthState)
                    && nearAthState.Ath > 0
                    && (double)price / nearAthState.Ath * 100.0 >= effectiveNearAthSuppressMinPercent)
                {
                    continue;
                }

                var passesBearish = SignalFollowThroughFilter.PassesBearish(
                    price,
                    lastAcceptedSellSignalPrice,
                    SignalFollowThroughThresholds.BearishMinFollowThroughPercent);

                if (persist && lastPersistedBuy != null)
                {
                    var sellLog = new SellActionLog
                    {
                        Price = price,
                        TradingDate = timestamp,
                        TradeAdvice = advice,
                        BuyActionLog = lastPersistedBuy,
                        IsValidated = passesBearish,
                        AlignedRanges = result.AlignedRanges,
                        TrendCountSum = result.TrendCountSum
                    };
                    await tradingRepository.SaveSellActionLog(sellLog, cancellationToken);
                    if (passesBearish) lastPersistedBuy = null;
                }

                if (!passesBearish)
                {
                    continue;
                }

                var profitPercent = simLatestBuyPrice.HasValue
                    ? PercentageService.GetPercentage(
                        (double)(price - simLatestBuyPrice.Value),
                        (double)simLatestBuyPrice.Value)
                    : null;

                trades.Add(new BacktestTrade
                {
                    Timestamp = timestamp,
                    TradeAdvice = advice,
                    Price = price,
                    ProfitPercent = profitPercent,
                    EntryTimestamp = entryTimestamp,
                    EntryPrice = simLatestBuyPrice,
                    HoldHours = entryTimestamp.HasValue ? (timestamp - entryTimestamp.Value).TotalHours : null,
                    MaxFavorableExcursionPercent = simLatestBuyPrice.HasValue && runMaxPrice > 0m
                        ? PercentageService.GetPercentage((double)(runMaxPrice - simLatestBuyPrice.Value), (double)simLatestBuyPrice.Value)
                        : null,
                    MaxAdverseExcursionPercent = simLatestBuyPrice.HasValue && runMinPrice > 0m
                        ? PercentageService.GetPercentage((double)(runMinPrice - simLatestBuyPrice.Value), (double)simLatestBuyPrice.Value)
                        : null,
                    EntryAlignedRanges = entryAlignedRanges,
                    EntryTrendCountSum = entryTrendCountSum
                });

                distributionState.OnExit(simLatestBuyPrice.Value, price);
                lastClosedEquity = lastClosedEquity * (double)price / (double)simLatestBuyPrice.Value;
                simLatestSellPrice = price;
                lastAcceptedSellSignalPrice = price;
                lastAcceptedBuySignalPrice = null;
                simLatestBuyPrice = null;
                simHasOpenPosition = false;
                lastSellTimestamp = timestamp;
                if (cycleTopForcedSell)
                    lastCycleTopForcedSellAt = timestamp;
                entryTimestamp = null;
                runMaxPrice = 0m;
                runMinPrice = 0m;
                entryAlignedRanges = null;
                entryTrendCountSum = null;
                simTrailingHighWaterMark = 0m;

                // Bei FireSell: Tracking zurücksetzen
                if (advice.Contains("FireSell"))
                {
                    simLowestBuyAfterFireSell = null;
                    simHighestPanicBuyAfterLowest = null;
                }
            }
        }

        return BuildSummary(trades, firstPrice, lastPrice, startDate, endDate, timestamps.Count,
            Math.Round(strategyMaxDrawdown, 2), Math.Round(hodlMaxDrawdown, 2));
    }

    private async Task<(IReadOnlyDictionary<DateOnly, bool> ZoneMap, IReadOnlyDictionary<DateOnly, double> AthMap)> PrecomputeCycleTopMapAsync(
        DateTime endDate,
        IReadOnlyList<DateTime> timestamps,
        CancellationToken cancellationToken)
    {
        var opts = bearMarketFilterOptions.Value;
        var dailyCandles = await binanceKlineService.GetCandlesUpToAsync(
            opts.CycleTopSellZoneInterval,
            endDate.ToUniversalTime(),
            cancellationToken);

        var lookbackDays = opts.CycleTopSellZoneLookbackDays;
        var minDrawup = opts.CycleTopSellZoneMinDrawupPercent;
        var minAthProximity = opts.CycleTopSellZoneMinAthProximityPercent;
        var minMonthsAfterHalving = opts.CycleTopSellZoneMinMonthsAfterHalving;
        var maxMonthsAfterHalving = opts.CycleTopSellZoneMaxMonthsAfterHalving;
        var confirmationDays = opts.CycleTopSellZoneConfirmationDays;
        var athTolerance = opts.CycleTopSellZoneConfirmationAthTolerancePercent / 100.0;
        var ordered = dailyCandles.OrderBy(c => c.CloseTime).ToArray();

        var map = new Dictionary<DateOnly, bool>();
        var athMap = new Dictionary<DateOnly, double>();
        // When confirmation is enabled, extend the evaluation window backward by N days so
        // that map[d-N] is available for every timestamp date d.
        var lookbackForConfirmation = Math.Max(0, confirmationDays);
        var distinctTimestampDates = timestamps
            .Select(t => DateOnly.FromDateTime(t.ToUniversalTime().Date))
            .Distinct()
            .ToHashSet();
        IEnumerable<DateOnly> uniqueDates;
        if (lookbackForConfirmation > 0 && distinctTimestampDates.Count > 0)
        {
            var minD = distinctTimestampDates.Min();
            var maxD = distinctTimestampDates.Max();
            var expanded = new HashSet<DateOnly>(distinctTimestampDates);
            for (var d = minD.AddDays(-lookbackForConfirmation); d < minD; d = d.AddDays(1))
            {
                expanded.Add(d);
            }
            // Also fill any gaps between minD and maxD so that map[d-N] is always present
            // for any d in [minD, maxD]. This is cheap and correct even for sparse timestamps.
            for (var d = minD; d <= maxD; d = d.AddDays(1))
            {
                expanded.Add(d);
            }
            uniqueDates = expanded;
        }
        else
        {
            uniqueDates = distinctTimestampDates;
        }

        foreach (var date in uniqueDates)
        {
            var asOfDt = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var cycleStart = asOfDt.AddDays(-lookbackDays);

            var fromIdx = BinarySearchFirstGe(ordered, cycleStart);
            var toIdx = BinarySearchLastLe(ordered, asOfDt);

            if (fromIdx < 0 || toIdx < fromIdx || (toIdx - fromIdx + 1) < 2)
            {
                map[date] = false;
                athMap[date] = 0.0;
                continue;
            }

            var cycleLow = double.MaxValue;
            var cycleAth = double.MinValue;
            var latestHigh = 0.0;
            var valid = true;

            for (var i = fromIdx; i <= toIdx; i++)
            {
                var c = ordered[i];
                if (c.Low <= 0 || c.High <= 0) { valid = false; break; }
                if ((double)c.Low < cycleLow) cycleLow = (double)c.Low;
                if ((double)c.High > cycleAth) cycleAth = (double)c.High;
                // Use the previous (fully-closed) candle's High as the zone trigger price.
                // ordered[toIdx] is today's daily candle (close = 23:59:59 UTC); using its
                // High would look ahead of the sell execution (first 2h bar ≈ 02:00 UTC).
                // Skipping the last iteration means latestHigh = ordered[toIdx-1].High,
                // which is yesterday's fully-closed candle — causal and executable.
                if (i < toIdx) latestHigh = (double)c.High;
            }

            if (!valid || cycleLow <= 0 || cycleAth <= 0 || latestHigh <= 0)
            {
                map[date] = false;
                athMap[date] = 0.0;
                continue;
            }

            var drawup = (latestHigh - cycleLow) / cycleLow * 100.0;
            var athProx = latestHigh / cycleAth * 100.0;
            var withinHalvingWindow = BitcoinConstants.IsWithinPostHalvingWindow(
                asOfDt, minMonthsAfterHalving, maxMonthsAfterHalving);
            map[date] = drawup >= minDrawup && athProx >= minAthProximity && withinHalvingWindow;
            athMap[date] = cycleAth;
        }

        // Confirmation-Window post-pass (ADR-008): require the raw zone to have been
        // armed N days ago. Optionally also require that the cycle ATH has not grown by
        // more than <see cref="BearMarketFilterOptions.CycleTopSellZoneConfirmationAthTolerancePercent"/>
        // between the two dates (skipped when tolerance &lt;= 0).
        //
        // Decoupled (2026-05-15): the "armed at both" check on its own filters short-lived
        // single-day spikes (the original concern). The ATH-growth check additionally
        // suppresses sells during still-rising-ATH tops — which kills correct top sells
        // in parabolic phases (e.g. 2025-05-23). Setting tolerance &lt;= 0 disables it
        // and keeps only the armed-at-both filter.
        if (confirmationDays > 0)
        {
            var applyAthToleranceCheck = athTolerance > 0;
            var rawZone = new Dictionary<DateOnly, bool>(map);
            foreach (var date in rawZone.Keys)
            {
                if (!rawZone[date])
                {
                    continue;
                }
                var armedDate = date.AddDays(-confirmationDays);
                if (!rawZone.TryGetValue(armedDate, out var armedRaw) || !armedRaw)
                {
                    map[date] = false;
                    continue;
                }
                if (!applyAthToleranceCheck)
                {
                    continue;
                }
                if (!athMap.TryGetValue(armedDate, out var armedAth) || armedAth <= 0)
                {
                    map[date] = false;
                    continue;
                }
                if (athMap[date] > armedAth * (1.0 + athTolerance))
                {
                    map[date] = false;
                }
            }
        }

        return (map, athMap);
    }

    private async Task<IReadOnlyDictionary<DateOnly, bool>> PrecomputePiCycleTopMapAsync(
        DateTime endDate,
        IReadOnlyList<DateTime> timestamps,
        CancellationToken cancellationToken)
    {
        const int sma111Period = 111;
        const int sma350Period = 350;

        var opts = bearMarketFilterOptions.Value;
        var dailyCandles = await binanceKlineService.GetCandlesUpToAsync(
            opts.PiCycleTopInterval,
            endDate.ToUniversalTime(),
            cancellationToken);

        // Pre-sorted array of (CloseTime, Close) for efficient scanning
        var ordered = dailyCandles.OrderBy(c => c.CloseTime).ToArray();
        var closes = ordered.Select(c => (double)c.Close).ToArray();

        var map = new Dictionary<DateOnly, bool>();
        var uniqueDates = timestamps
            .Select(t => DateOnly.FromDateTime(t.ToUniversalTime().Date))
            .Distinct()
            .OrderBy(d => d)
            .ToArray();

        // Pre-compute full SMA(111) and SMA(350) series once using a simple rolling sum (O(n))
        var sma111Full = ComputeSimpleSmaFull(closes, sma111Period);
        var sma350Full = ComputeSimpleSmaFull(closes, sma350Period);

        foreach (var date in uniqueDates)
        {
            var asOfDt = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var toIdx = BinarySearchLastLe(ordered, asOfDt);

            if (toIdx < sma350Period - 1)
            {
                // Not enough history
                map[date] = false;
                continue;
            }

            var s111 = sma111Full[toIdx];
            var s350 = sma350Full[toIdx];

            if (!s111.HasValue || !s350.HasValue || s350.Value <= 0)
            {
                map[date] = false;
                continue;
            }

            map[date] = s111.Value >= s350.Value * 2.0;
        }

        return map;
    }

    private async Task<IReadOnlyDictionary<DateOnly, bool>> PrecomputePuellMultipleMapAsync(
        DateTime endDate,
        IReadOnlyList<DateTime> timestamps,
        CancellationToken cancellationToken)
    {
        const int smaPeriod = 365;
        var opts = bearMarketFilterOptions.Value;
        var dailyCandles = await binanceKlineService.GetCandlesUpToAsync(
            opts.PuellMultipleInterval,
            endDate.ToUniversalTime(),
            cancellationToken);

        var ordered = dailyCandles.OrderBy(c => c.CloseTime).ToArray();
        var closes = ordered.Select(c => (double)c.Close).ToArray();

        var map = new Dictionary<DateOnly, bool>();
        var uniqueDates = timestamps
            .Select(t => DateOnly.FromDateTime(t.ToUniversalTime().Date))
            .Distinct()
            .OrderBy(d => d)
            .ToArray();

        var sma365Full = ComputeSimpleSmaFull(closes, smaPeriod);

        foreach (var date in uniqueDates)
        {
            var asOfDt = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var toIdx = BinarySearchLastLe(ordered, asOfDt);

            if (toIdx < smaPeriod - 1)
            {
                map[date] = false;
                continue;
            }

            var sma365 = sma365Full[toIdx];
            if (!sma365.HasValue || sma365.Value <= 0)
            {
                map[date] = false;
                continue;
            }

            // Puell ≈ currentClose / SMA(365): below threshold = deep accumulation zone
            var puellApprox = closes[toIdx] / sma365.Value;
            map[date] = puellApprox < opts.PuellMultipleBuyBoostThreshold;
        }

        return map;
    }

    private static bool ShouldForceCycleTopSellFromMap(
        string tradeAdvice,
        DateTime timestamp,
        bool hasOpenPosition,
        IReadOnlyDictionary<DateOnly, bool> cycleTopMap)
    {
        if (!hasOpenPosition)
            return false;

        if (tradeAdvice.Contains("Sell", StringComparison.OrdinalIgnoreCase)
            || tradeAdvice.Contains("HardStop", StringComparison.OrdinalIgnoreCase)
            || tradeAdvice.Contains("TrailingStop", StringComparison.OrdinalIgnoreCase))
            return false;

        var date = DateOnly.FromDateTime(timestamp.ToUniversalTime().Date);
        return cycleTopMap.TryGetValue(date, out var overvalued) && overvalued;
    }

    private static int BinarySearchFirstGe(OhlcvCandle[] ordered, DateTime target)
    {
        int lo = 0, hi = ordered.Length - 1, result = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (ordered[mid].CloseTime >= target) { result = mid; hi = mid - 1; }
            else lo = mid + 1;
        }
        return result;
    }

    private static int BinarySearchLastLe(OhlcvCandle[] ordered, DateTime target)
    {
        int lo = 0, hi = ordered.Length - 1, result = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (ordered[mid].CloseTime <= target) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result;
    }

    /// <summary>
    /// Computes a simple (unweighted) SMA series for the full input in O(n).
    /// Returns null for indices where fewer than <paramref name="period"/> values are available.
    /// </summary>
    private static double?[] ComputeSimpleSmaFull(double[] closes, int period)
    {
        var result = new double?[closes.Length];
        var sum = 0.0;
        for (var i = 0; i < closes.Length; i++)
        {
            sum += closes[i];
            if (i >= period)
                sum -= closes[i - period];
            if (i >= period - 1)
                result[i] = sum / period;
        }
        return result;
    }

    /// <summary>
    /// Pre-computes a DateOnly→bool map indicating whether a given day is in a bear market
    /// (close price below the smaPeriod-day SMA). Avoids per-timestamp lock acquisitions on
    /// BinanceKlineService and O(n²) LINQ sorts inside IsBearMarketAsync.
    /// </summary>
    private static IReadOnlyDictionary<DateOnly, bool> BuildBearMarketMap(
        IReadOnlyList<OhlcvCandle> dailyCandles,
        int smaPeriod,
        IReadOnlyList<DateTime> timestamps)
    {
        var ordered = dailyCandles.OrderBy(c => c.CloseTime).ToArray();
        // Rolling SMA using a running sum over the window.
        var smaAtIndex = new double?[ordered.Length];
        var rollingSum = 0.0;
        for (var i = 0; i < ordered.Length; i++)
        {
            rollingSum += ordered[i].Close;
            if (i >= smaPeriod) rollingSum -= ordered[i - smaPeriod].Close;
            if (i >= smaPeriod - 1) smaAtIndex[i] = rollingSum / smaPeriod;
        }

        var map = new Dictionary<DateOnly, bool>();
        var uniqueDates = timestamps
            .Select(t => DateOnly.FromDateTime(t.ToUniversalTime().Date))
            .Distinct();

        foreach (var date in uniqueDates)
        {
            // Mirror GetCandlesUpToAsync("1d", 2h-timestamp): use candles closed BEFORE this date.
            var beforeStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddMilliseconds(-1);
            var idx = BinarySearchLastLe(ordered, beforeStart);
            if (idx < smaPeriod - 1 || smaAtIndex[idx] is not { } sma)
            {
                map[date] = false;
                continue;
            }

            map[date] = ordered[idx].Close < sma;
        }

        return map;
    }

    /// <summary>
    /// Pre-computes a DateOnly→bool map indicating whether a given day is in a "deep cycle low"
    /// (drawdown from running ATH exceeds the per-day threshold). Uses a running-max to achieve
    /// O(n + m) complexity instead of O(n·m) from per-timestamp LINQ scans.
    /// </summary>
    private static IReadOnlyDictionary<DateOnly, bool> BuildDeepCycleLowMap(
        IReadOnlyList<OhlcvCandle> candles,
        IReadOnlyList<DateTime> timestamps,
        Func<DateTime, double> thresholdFunc)
    {
        var ordered = candles.OrderBy(c => c.CloseTime).ToArray();
        var map = new Dictionary<DateOnly, bool>();
        var uniqueDates = timestamps
            .Select(t => DateOnly.FromDateTime(t.ToUniversalTime().Date))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var runningAth = double.MinValue;
        var prevIdx = -1;

        foreach (var date in uniqueDates)
        {
            var asOf = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var toIdx = BinarySearchLastLe(ordered, asOf);

            if (toIdx < 1)
            {
                map[date] = false;
                continue;
            }

            // Extend running ATH for candles not yet seen.
            for (var i = prevIdx + 1; i <= toIdx; i++)
            {
                if (ordered[i].High > runningAth) runningAth = ordered[i].High;
            }

            prevIdx = toIdx;

            if (runningAth <= 0)
            {
                map[date] = false;
                continue;
            }

            var latestLow = ordered[toIdx].Low;
            var drawdownPct = (latestLow - runningAth) / runningAth * 100.0;
            var threshold = thresholdFunc(asOf);
            var fires = drawdownPct <= -threshold;
            map[date] = fires;
        }

        return map;
    }

    /// <summary>
    /// Precomputes per-date map: true = current close is above maxProximityPercent of running ATH → buy blocked.
    /// The running ATH advances causally (only includes candle highs up to and including each date).
    /// Fail-open: dates with fewer than 2 historical candles map to false.
    /// Builds a causal map from each backtest timestamp to the running ATH state: the max High value
    /// and the CloseTime of the candle that set it. This is used so the buy gate can compare the
    /// actual signal price against the ATH and also check how recently the ATH was set.
    /// </summary>
    private static IReadOnlyDictionary<DateTime, (double Ath, DateTime AthSetAt)> BuildRunningAthMap(
        IReadOnlyList<OhlcvCandle> candles,
        IReadOnlyList<DateTime> timestamps)
    {
        var ordered = candles.OrderBy(c => c.CloseTime).ToArray();
        var map = new Dictionary<DateTime, (double, DateTime)>(timestamps.Count);
        var sortedTimestamps = timestamps.OrderBy(t => t).ToList();

        var runningAth = double.MinValue;
        var athSetAt = DateTime.MinValue;
        var prevIdx = -1;

        foreach (var ts in sortedTimestamps)
        {
            var toIdx = BinarySearchLastLe(ordered, ts);

            if (toIdx < 0)
            {
                map[ts] = (0, DateTime.MinValue);
                continue;
            }

            for (var i = prevIdx + 1; i <= toIdx; i++)
            {
                if (ordered[i].High > runningAth)
                {
                    runningAth = ordered[i].High;
                    athSetAt = ordered[i].CloseTime;
                }
            }

            prevIdx = toIdx;
            map[ts] = runningAth > 0 ? (runningAth, athSetAt) : (0, DateTime.MinValue);
        }

        return map;
    }

    private async Task<IReadOnlyList<DateTime>> GetBacktestTimestampsAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var startUtc = startDate.ToUniversalTime();
        var endUtc = endDate.ToUniversalTime();

        var candles2h = await binanceKlineService.GetCandlesUpToAsync("2h", endUtc, cancellationToken);

        return candles2h
            .Select(c => c.CloseTime)
            .Where(ts => ts >= startUtc && ts <= endUtc)
            .Distinct()
            .OrderBy(ts => ts)
            .ToList();
    }

    private async Task<IReadOnlyDictionary<DateTime, OhlcvCandle>> BuildCandleMapAsync(
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var candles = await binanceKlineService.GetCandlesUpToAsync("2h", endDate.ToUniversalTime(), cancellationToken);
        var map = new Dictionary<DateTime, OhlcvCandle>(candles.Count);
        foreach (var c in candles)
        {
            map[c.CloseTime] = c;
        }
        return map;
    }

    /// <summary>
    /// Fetches 1-day candles and returns a lookup: DateOnly → true if the closing price was below
    /// the 200-day SMA on that date. Used by the SMA-hold exit debounce gate.
    /// </summary>
    private async Task<Dictionary<DateOnly, bool>> BuildDailyBelowSma200LookupAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        const int SmaPeriod = 200;
        // Need 200 days of warm-up before startDate.
        var fetchFrom = startDate.AddDays(-(SmaPeriod + 5));
        var candles = await binanceKlineService.GetCandlesRangeAsync("1d", fetchFrom.ToUniversalTime(), endDate.ToUniversalTime(), cancellationToken);
        var sorted = candles.OrderBy(c => c.CloseTime).ToList();

        var result = new Dictionary<DateOnly, bool>(sorted.Count);
        for (var i = SmaPeriod - 1; i < sorted.Count; i++)
        {
            var sma = 0m;
            for (var j = i - SmaPeriod + 1; j <= i; j++)
                sma += (decimal)sorted[j].Close;
            sma /= SmaPeriod;
            var date = DateOnly.FromDateTime(sorted[i].CloseTime.ToUniversalTime());
            result[date] = (decimal)sorted[i].Close < sma;
        }
        return result;
    }

    /// <summary>
    /// Builds a daily lookup of close/SMA(<paramref name="smaPeriod"/>d) ratio.
    /// Used by the PostCycleTopSmaRatioBlock gate to suppress re-entries while price
    /// remains above the 2-year MA multiplier threshold.
    /// </summary>
    private async Task<Dictionary<DateOnly, double>> BuildDailySmaRatioLookupAsync(
        DateTime startDate,
        DateTime endDate,
        int smaPeriod,
        CancellationToken cancellationToken)
    {
        var fetchFrom = startDate.AddDays(-(smaPeriod + 5));
        var candles = await binanceKlineService.GetCandlesRangeAsync("1d", fetchFrom.ToUniversalTime(), endDate.ToUniversalTime(), cancellationToken);
        var sorted = candles.OrderBy(c => c.CloseTime).ToList();

        var result = new Dictionary<DateOnly, double>(sorted.Count);
        for (var i = smaPeriod - 1; i < sorted.Count; i++)
        {
            var sma = 0.0;
            for (var j = i - smaPeriod + 1; j <= i; j++)
                sma += sorted[j].Close;
            sma /= smaPeriod;
            if (sma <= 0) continue;
            var date = DateOnly.FromDateTime(sorted[i].CloseTime.ToUniversalTime());
            result[date] = sorted[i].Close / sma;
        }
        return result;
    }

    private static BacktestSummary BuildSummary(
        List<BacktestTrade> trades,
        decimal? firstPrice,
        decimal? lastPrice,
        DateTime startDate,
        DateTime endDate,
        int timestampsEvaluated,
        double strategyMaxDrawdown = 0.0,
        double hodlMaxDrawdown = 0.0)
    {
        var sells = trades.Where(t => IsSellAdvice(t.TradeAdvice) && t.ProfitPercent.HasValue).ToList();
        var totalProfit = sells.Any() ? sells.Sum(t => t.ProfitPercent!.Value) : (double?)null;
        var wins = sells.Count(t => t.ProfitPercent > 0);
        var winRate = sells.Count > 0 ? (double)wins / sells.Count * 100 : (double?)null;
        var maxLoss = sells.Any() ? sells.Min(t => t.ProfitPercent!.Value) : (double?)null;
        var maxGain = sells.Any() ? sells.Max(t => t.ProfitPercent!.Value) : (double?)null;

        // Compounded return: ∏(1 + r_i/100) − 1
        double compoundedEquity = 1.0;
        foreach (var s in sells) compoundedEquity *= (1.0 + s.ProfitPercent!.Value / 100.0);
        double? compoundedProfit = sells.Any() ? Math.Round((compoundedEquity - 1.0) * 100.0, 2) : null;

        // Time in market: sum of holding durations of closed trades + open position
        double? timeInMarket = null;
        var totalPeriodHours = (endDate - startDate).TotalHours;
        if (totalPeriodHours > 0)
        {
            var heldHours = sells
                .Where(s => s.EntryTimestamp.HasValue)
                .Sum(s => (s.Timestamp - s.EntryTimestamp!.Value).TotalHours);
            // Count open position if still active at end
            var lastBuy = trades.LastOrDefault(t => t.TradeAdvice.Contains("Buy"));
            if (lastBuy != null && !sells.Any(s => s.EntryTimestamp >= lastBuy.Timestamp))
                heldHours += (endDate - lastBuy.Timestamp).TotalHours;
            timeInMarket = Math.Round(heldHours / totalPeriodHours * 100.0, 1);
        }

        double? buyAndHold = firstPrice.HasValue && lastPrice.HasValue && firstPrice > 0
            ? PercentageService.GetPercentage((double)(lastPrice.Value - firstPrice.Value), (double)firstPrice.Value)
            : null;

        return new BacktestSummary
        {
            StartDate = startDate,
            EndDate = endDate,
            TimestampsEvaluated = timestampsEvaluated,
            FirstPrice = firstPrice,
            LastPrice = lastPrice,
            TotalBuys = trades.Count(t => t.TradeAdvice.Contains("Buy")),
            TotalSells = trades.Count(t => IsSellAdvice(t.TradeAdvice)),
            TotalProfitPercent = totalProfit.HasValue ? Math.Round(totalProfit.Value, 2) : null,
            WinRatePercent = winRate.HasValue ? Math.Round(winRate.Value, 1) : null,
            MaxLossPercent = maxLoss.HasValue ? Math.Round(maxLoss.Value, 2) : null,
            MaxGainPercent = maxGain.HasValue ? Math.Round(maxGain.Value, 2) : null,
            BuyAndHoldPercent = buyAndHold.HasValue ? Math.Round(buyAndHold.Value, 2) : null,
            CompoundedProfitPercent = compoundedProfit,
            TimeInMarketPercent = timeInMarket,
            MaxDrawdownPercent = strategyMaxDrawdown > 0 ? strategyMaxDrawdown : null,
            HodlMaxDrawdownPercent = hodlMaxDrawdown > 0 ? hodlMaxDrawdown : null,
            Trades = trades
        };
    }

    private static bool IsSellAdvice(string tradeAdvice)
    {
        return tradeAdvice.Contains("Sell", StringComparison.OrdinalIgnoreCase)
            || tradeAdvice.Equals("HardStopLoss", StringComparison.OrdinalIgnoreCase)
            || tradeAdvice.Equals("TrailingStop", StringComparison.OrdinalIgnoreCase)
            || tradeAdvice.Equals("BreakevenStop", StringComparison.OrdinalIgnoreCase);
    }

    private static void CountAdvice(string advice, ref int buyCount, ref int sellCount, ref int waitCount)
    {
        if (advice.Contains("Buy", StringComparison.OrdinalIgnoreCase))
        {
            buyCount++;
            return;
        }

        if (IsSellAdvice(advice))
        {
            sellCount++;
            return;
        }

        waitCount++;
    }

    private async Task<string> ApplyRegimeWithBonusAsync(
        BitcoinTradeStatus result,
        MarketRegime regime,
        double bearishRegimeMinLinearBullishScore,
        DateTime timestamp,
        CancellationToken cancellationToken,
        bool bullishRegimeBlockBuy = false)
    {
        var opts = volumeVolatilityOptions.Value;
        if (!opts.EnableScoreBonus)
        {
            return MarketRegimeDecisionFilter.Apply(result, regime, bearishRegimeMinLinearBullishScore, bullishRegimeBlockBuy);
        }

        var snapshot = await marketContextService.GetSnapshotAsync(opts.Interval, timestamp, cancellationToken);
        var (bullishBonus, _) = LinearScoreBonusCalculator.Compute(snapshot, opts);
        return MarketRegimeDecisionFilter.Apply(result, regime, bearishRegimeMinLinearBullishScore, bullishBonus, bullishRegimeBlockBuy);
    }

    private static string BuildTupleKey(int alignedRanges, int trendCountSum)
    {
        return $"A{alignedRanges}_T{trendCountSum}";
    }

    private static bool TryResolveNextCandleOpen(
        IReadOnlyList<OhlcvCandle> candles,
        DateTime signalTimestampUtc,
        out DateTime executionTimestampUtc,
        out decimal executionPrice)
    {
        executionTimestampUtc = default;
        executionPrice = 0m;

        var next = candles
            .Where(c => c.OpenTime > signalTimestampUtc)
            .OrderBy(c => c.OpenTime)
            .FirstOrDefault();
        if (next == null)
        {
            return false;
        }

        executionTimestampUtc = DateTime.SpecifyKind(next.OpenTime, DateTimeKind.Utc);
        executionPrice = (decimal)next.Open;
        return executionPrice > 0m;
    }

    // Binärsuche: neueste Candle mit Timestamp <= dateTime
    private static (decimal? strategyHigh, decimal? strategyLow) LookupMicrostrategyPrice(
        IReadOnlyList<StockCandle> sortedCandles, DateTime dateTime)
    {
        if (sortedCandles.Count == 0) return (null, null);

        int lo = 0, hi = sortedCandles.Count - 1, result = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (sortedCandles[mid].Timestamp <= dateTime)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (result == -1) return (null, null);
        var candle = sortedCandles[result];
        return (candle.HighPrice, candle.LowPrice);
    }

    private Task<IReadOnlyCollection<BitcoinTradeStatus>> GetBitcoinTradeAdviceAsync(
        DateTime timestamp,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? strategyHigh,
        decimal? strategyLow,
        BuyActionLog? lowestBuyAfterFireSell,
        BuyActionLog? highestPanicBuyAfterLowest,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet,
        CancellationToken cancellationToken)
    {
        if (confluenceThresholdSet == LongTimeframeConfluenceThresholdSet.Default)
        {
            return bitcoinTradeAdvisor.GetBitcoinTradeAdvice(
                timestamp,
                timestamp,
                latestBuyPrice,
                latestSellPrice,
                strategyHigh,
                strategyLow,
                lowestBuyAfterFireSell,
                highestPanicBuyAfterLowest,
                cancellationToken);
        }

        return bitcoinTradeAdvisor.GetBitcoinTradeAdvice(
            timestamp,
            timestamp,
            latestBuyPrice,
            latestSellPrice,
            strategyHigh,
            strategyLow,
            lowestBuyAfterFireSell,
            highestPanicBuyAfterLowest,
            cancellationToken,
            confluenceThresholdSet);
    }
}
