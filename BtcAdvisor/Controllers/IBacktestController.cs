using Microsoft.AspNetCore.Mvc;
using BtcAdvisor.Services.Conditions;

namespace BtcAdvisor.Controllers;

public interface IBacktestController
{
    Task<IActionResult> RunBacktest(
        DateTime startDate,
        DateTime endDate,
        bool persist,
        double momentumBrakeMaxRatio = DistributionTopThresholds.MomentumBrakeMaxRatio,
        double troughMinDrawdownPercent = DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
        double troughRecoveryProximityPercent = DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
        int bullishMinAlignedRanges = LongTimeframeThresholds.BullishMinAlignedRanges,
        int bullishMaxAlignedRanges = int.MaxValue,
        int bullishMinAlignedAnchorRanges = LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
        int bullishMinTrendCountSum = LongTimeframeThresholds.BullishMinTrendCountSum,
        int bullishMaxTrendCountSum = int.MaxValue,
        int bearishMinAlignedRanges = LongTimeframeThresholds.BearishMinAlignedRanges,
        int bearishMinTrendCountSum = LongTimeframeThresholds.BearishMinTrendCountSum,
        double bearishRegimeMinLinearBullishScore = MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
        int reEntryCooldownHours = -1,
        int smaExitDebounceDays = 0,
        bool bullishRegimeBlockBuy = false,
        bool entryRequiresPriceAboveSma200 = false,
        double breakevenTriggerPercent = 0.0,
        double trailingStopPercent = 0.0,
        double trailingStopActivationPercent = 0.0,
        double nearAthSellSuppressMinPercent = 0.0,
        int athProximityBuyBlockMaxAthAgeDays = 0,
        bool macroOnlyMode = false,
        CancellationToken cancellationToken = default);

    Task<IActionResult> SignalReturns(
        DateTime startDate,
        DateTime endDate,
        int bullishMinAlignedRanges = LongTimeframeThresholds.BullishMinAlignedRanges,
        int bullishMaxAlignedRanges = int.MaxValue,
        int bullishMinAlignedAnchorRanges = LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
        int bullishMinTrendCountSum = LongTimeframeThresholds.BullishMinTrendCountSum,
        int bullishMaxTrendCountSum = int.MaxValue,
        int bearishMinAlignedRanges = LongTimeframeThresholds.BearishMinAlignedRanges,
        int bearishMinTrendCountSum = LongTimeframeThresholds.BearishMinTrendCountSum,
        CancellationToken cancellationToken = default);
}
