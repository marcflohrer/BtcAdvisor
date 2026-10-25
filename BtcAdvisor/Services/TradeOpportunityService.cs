using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;
using BtcAdvisor.Constants;
using Microsoft.Extensions.Options;
using static BtcAdvisor.Extensions.DateTimeExtensions;
using static BtcAdvisor.Services.Extensions.DealExtensions;

namespace BtcAdvisor.Services;

public class TradeOpportunityService : ITradeOpportunityService
{
    private readonly ITradingRepository _tradingRepository;
    private readonly ITelegramBotService _telegramBotService;
    private readonly IActionLogService _actionLogService;
    private readonly IBinanceKlineService _binanceKlineService;
    private readonly IBearMarketFilterService _bearMarketFilterService;
    private readonly BearMarketFilterOptions _bearMarketFilterOptions;
    private readonly RiskControlOptions _riskControlOptions;

    public TradeOpportunityService(
        ITradingRepository tradingRepository,
        ITelegramBotService telegramBotService,
        IActionLogService actionLogService,
        IBinanceKlineService binanceKlineService,
        IBearMarketFilterService bearMarketFilterService,
        IOptions<BearMarketFilterOptions> bearMarketFilterOptions,
        IOptions<RiskControlOptions> riskControlOptions)
    {
        _tradingRepository = tradingRepository;
        _telegramBotService = telegramBotService;
        _actionLogService = actionLogService;
        _binanceKlineService = binanceKlineService;
        _bearMarketFilterService = bearMarketFilterService;
        _bearMarketFilterOptions = bearMarketFilterOptions.Value;
        _riskControlOptions = riskControlOptions.Value;
    }

    public async Task TradeOpportunity(Deal deal, CancellationToken cancellationToken)
    {
        try
        {
            var utcTradingDate = deal.TradingDate.ToUtcDateTime();
            var (latestSell, latestBuy) = await _tradingRepository.FindLatestAction(utcTradingDate);

            if (deal.TradeAdvice.Equals("HardStopLoss", StringComparison.OrdinalIgnoreCase))
            {
                await HandleHardStopLoss(deal, utcTradingDate, latestBuy, cancellationToken);
            }
            else if (_bearMarketFilterOptions.MacroOnlyMode)
            {
                // macroOnly mode: bypass the full micro-signal advisor; derive advice from macro
                // maps only — matching the Phase 5b backtest strategy that beats HODL (+2,831 %).
                // ADR-017.
                var hasOpenPosition = HasOpenPosition(latestSell, latestBuy);
                var cycleTopForcedSell = false;

                // 1. CycleTopZone → DistributionSell when position is open
                if (hasOpenPosition
                    && await ShouldForceCycleTopSellAsync("Wait", utcTradingDate, latestSell, latestBuy, cancellationToken))
                {
                    deal.TradeAdvice = "DistributionSell";
                    cycleTopForcedSell = true;
                    await HandleSell(deal, utcTradingDate, latestSell, latestBuy, cycleTopForcedSell, cancellationToken);
                    return;
                }

                if (!hasOpenPosition)
                {
                    // 2. DeepCycleLow → StrongBuy
                    var isDeepLow = _bearMarketFilterOptions.AthDrawdownOverrideEnabled
                        && await _bearMarketFilterService.IsDeepCycleLowAsync(
                            _bearMarketFilterOptions.AthDrawdownInterval,
                            utcTradingDate,
                            AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(_bearMarketFilterOptions, utcTradingDate),
                            cancellationToken);
                    if (isDeepLow)
                    {
                        deal.TradeAdvice = "StrongBuy";
                        await HandleBuy(deal, utcTradingDate, latestBuy, latestSell, cancellationToken);
                        return;
                    }

                    // 3. ATH-breakout re-entry: after a DistributionSell that was NOT opened by
                    //    AthBreakoutBuy, buy when current price exceeds the prior cycle ATH by 1 %.
                    //    Prevents chaining: if the last entry that was sold was itself an
                    //    AthBreakoutBuy the signal is suppressed (simLastEntryWasAthBreakout).
                    var lastEntryWasAthBreakout =
                        latestSell?.BuyActionLog?.TradeAdvice == "AthBreakoutBuy";
                    if (!lastEntryWasAthBreakout
                        && latestSell?.TradeAdvice == "DistributionSell")
                    {
                        var sellDate = latestSell.TradingDate.Kind == DateTimeKind.Utc
                            ? latestSell.TradingDate
                            : latestSell.TradingDate.ToUniversalTime();
                        var cycleAth = await _bearMarketFilterService.GetCycleAthAsync(
                            _bearMarketFilterOptions.CycleTopSellZoneInterval,
                            sellDate,
                            _bearMarketFilterOptions.CycleTopSellZoneLookbackDays,
                            cancellationToken);
                        if (cycleAth > 0 && (double)deal.Price > cycleAth * 1.01)
                        {
                            deal.TradeAdvice = "AthBreakoutBuy";
                            await HandleBuy(deal, utcTradingDate, latestBuy, latestSell, cancellationToken);
                            return;
                        }
                    }
                }
                // All other cases: Wait — do nothing
                Console.WriteLine($"MacroOnly Wait: {deal.Price:N0} {deal.TradingDate}");
            }
        }
        catch (NullReferenceException ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            throw;
        }
    }

    private async Task HandleHardStopLoss(
        Deal deal,
        DateTime utcTradingDate,
        BuyActionLog? latestBuy,
        CancellationToken cancellationToken)
    {
        if (latestBuy == null) return;
        var profitAndBuyDeal = deal.GetProfit(latestBuy);
        await ProcessSell(deal, utcTradingDate, profitAndBuyDeal.BuyActionLog, profitAndBuyDeal.Profit, shouldSendSellMessage: true, cancellationToken);
    }

    private async Task HandleSell(
        Deal deal,
        DateTime utcTradingDate,
        SellActionLog? latestSell,
        BuyActionLog? latestBuy,
        bool cycleTopForcedSell,
        CancellationToken cancellationToken)
    {
        if (!HasOpenPosition(latestSell, latestBuy))
        {
            return;
        }

        // Minimum hold: suppress regular Sell/DistributionSell before the minimum hold duration.
        // FireSell, HardStopLoss, and TrailingStop bypass this gate.
        if (_riskControlOptions.MinHoldHours > 0
            && latestBuy != null
            && !deal.TradeAdvice.Contains("FireSell")
            && !deal.TradeAdvice.Contains("HardStop")
            && !deal.TradeAdvice.Contains("TrailingStop")
            && (utcTradingDate - latestBuy.TradingDate).TotalHours < _riskControlOptions.MinHoldHours)
        {
            return;
        }

        if (!SignalFollowThroughFilter.PassesBearish(
                deal.Price,
                previousAcceptedPrice: null,
                SignalFollowThroughThresholds.BearishMinFollowThroughPercent))
        {
            return;
        }

        // Deep-cycle-low sell suppression: regular bearish exits are held in the zone.
        // Emergency exits (FireSell, HardStopLoss, TrailingStop) bypass this gate.
        if (_bearMarketFilterOptions.AthDrawdownSuppressBearishSellEnabled
            && !deal.TradeAdvice.Contains("FireSell")
            && !deal.TradeAdvice.Contains("HardStop")
            && !deal.TradeAdvice.Contains("TrailingStop")
            && await _bearMarketFilterService.IsDeepCycleLowAsync(
                _bearMarketFilterOptions.AthDrawdownSuppressInterval,
                utcTradingDate,
                AthDrawdownThresholdPolicy.ResolveSuppressThresholdPercent(_bearMarketFilterOptions, utcTradingDate),
                cancellationToken))
        {
            return;
        }

        // Near-ATH sell suppression: when price is still near the ATH the bull run is likely
        // ongoing. Regular Sell/DistributionSell are suppressed until the position exits via
        // FireSell, HardStopLoss, or TrailingStop.
        // Cycle-top forced sells (PiCycleTop, CycleTopZone) bypass this gate — these are
        // macro-timing signals specifically designed to exit at the top.
        if (_bearMarketFilterOptions.NearAthSellSuppressEnabled
            && !deal.TradeAdvice.Contains("FireSell")
            && !deal.TradeAdvice.Contains("HardStop")
            && !deal.TradeAdvice.Contains("TrailingStop")
            && !cycleTopForcedSell
            && await _bearMarketFilterService.IsNearAthForSellSuppressAsync(
                _bearMarketFilterOptions.NearAthSellSuppressInterval,
                utcTradingDate,
                _bearMarketFilterOptions.NearAthSellSuppressMinPercent,
                cancellationToken))
        {
            return;
        }

        var profitAndBuyDeal = deal.GetProfit(latestBuy);
        await ProcessSell(deal, utcTradingDate, profitAndBuyDeal.BuyActionLog, profitAndBuyDeal.Profit, shouldSendSellMessage: true, cancellationToken);
    }

    private async Task ProcessSell(
        Deal deal,
        DateTime utcTradingDate,
        BuyActionLog? buyActionLog,
        string profit,
        bool shouldSendSellMessage,
        CancellationToken cancellationToken)
    {
        var sellActionLog = CreateSellActionLog(deal, utcTradingDate, buyActionLog, shouldSendSellMessage);
        await _tradingRepository.SaveSellActionLog(sellActionLog, cancellationToken);

        if (shouldSendSellMessage)
        {
            await SendSellMessage(deal, profit);
        }
    }

    private async Task HandleBuy(
        Deal deal,
        DateTime utcTradingDate,
        BuyActionLog? latestBuy,
        SellActionLog? latestSell,
        CancellationToken cancellationToken)
    {
        if (HasOpenPosition(latestSell, latestBuy))
        {
            return;
        }

        if (!SignalFollowThroughFilter.PassesBullish(
                deal.Price,
                previousAcceptedPrice: null,
                SignalFollowThroughThresholds.BullishMinFollowThroughPercent))
        {
            return;
        }

        var deepCycleOverrideTriggered = false;
        if (_bearMarketFilterOptions.Enabled)
        {
            var isBearMarket = await _bearMarketFilterService.IsBearMarketAsync(
                _bearMarketFilterOptions.Interval,
                utcTradingDate,
                _bearMarketFilterOptions.MovingAveragePeriod,
                cancellationToken);
            if (isBearMarket)
            {
                deepCycleOverrideTriggered = _bearMarketFilterOptions.AthDrawdownOverrideEnabled
                    && await _bearMarketFilterService.IsDeepCycleLowAsync(
                        _bearMarketFilterOptions.AthDrawdownInterval,
                        utcTradingDate,
                        AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(_bearMarketFilterOptions, utcTradingDate),
                        cancellationToken);
                if (!deepCycleOverrideTriggered)
                {
                    // Additional override: Puell Multiple below threshold (price at cycle-floor valuation)
                    var puellOverride = _bearMarketFilterOptions.PuellMultipleBuyBoostEnabled
                        && await _bearMarketFilterService.IsPuellMultipleUndervaluedAsync(
                            _bearMarketFilterOptions.PuellMultipleInterval,
                            utcTradingDate,
                            _bearMarketFilterOptions.PuellMultipleBuyBoostThreshold,
                            cancellationToken);
                    if (!puellOverride)
                    {
                        return;
                    }
                }
            }
        }

        if (deepCycleOverrideTriggered)
        {
            var deferredBuy = await TryResolveNextCandleOpenAsync(
                _bearMarketFilterOptions.AthDrawdownInterval,
                utcTradingDate,
                cancellationToken);
            if (!deferredBuy.HasValue)
            {
                // No following candle yet; defer until next realtime tick.
                return;
            }

            utcTradingDate = deferredBuy.Value.TimestampUtc;
            deal.Price = deferredBuy.Value.OpenPrice;
        }

        var cooldownHours = _riskControlOptions.ReEntryCooldownHours;
        if (cooldownHours > 0 && latestSell != null)
        {
            var sellDate = latestSell.TradingDate.Kind == DateTimeKind.Utc
                ? latestSell.TradingDate
                : latestSell.TradingDate.ToUniversalTime();
            if ((utcTradingDate - sellDate).TotalHours < cooldownHours)
            {
                return;
            }
        }

        var isAthBreakoutBuy = deal.TradeAdvice == "AthBreakoutBuy";

        // Block new entry while Pi Cycle Top or Cycle Top zone is active — entering
        // would immediately trigger a DistributionSell (tight-loop guard, ADR-008).
        // AthBreakoutBuy bypasses both: the old ATH is now support, not resistance;
        // buying above it is the macro thesis (ADR-017).
        if (!isAthBreakoutBuy
            && _bearMarketFilterOptions.PiCycleTopSellEnabled
            && await _bearMarketFilterService.IsPiCycleTopAsync(
                _bearMarketFilterOptions.PiCycleTopInterval,
                utcTradingDate,
                cancellationToken))
        {
            return;
        }

        if (!isAthBreakoutBuy
            && _bearMarketFilterOptions.CycleTopSellZoneEnabled
            && BitcoinConstants.IsWithinPostHalvingWindow(
                utcTradingDate,
                _bearMarketFilterOptions.CycleTopSellZoneMinMonthsAfterHalving,
                _bearMarketFilterOptions.CycleTopSellZoneMaxMonthsAfterHalving)
            && await _bearMarketFilterService.IsCycleTopOvervaluedAsync(
                _bearMarketFilterOptions.CycleTopSellZoneInterval,
                utcTradingDate,
                _bearMarketFilterOptions.CycleTopSellZoneLookbackDays,
                _bearMarketFilterOptions.CycleTopSellZoneMinDrawupPercent,
                _bearMarketFilterOptions.CycleTopSellZoneMinAthProximityPercent,
                cancellationToken)
            && await IsCycleTopConfirmationSatisfiedAsync(utcTradingDate, cancellationToken))
        {
            return;
        }

        // Post-cycle-top buy block: suppress re-entries for N days after a forced
        // cycle-top sell (DistributionSell from PiCycleTop / CycleTopZone).
        // AthBreakoutBuy bypasses this gate — the ATH breakout is the re-entry signal (ADR-017).
        if (!isAthBreakoutBuy
            && _bearMarketFilterOptions.PostCycleTopBuyBlockEnabled
            && latestSell != null
            && latestSell.TradeAdvice == "DistributionSell"
            && _bearMarketFilterOptions.PostCycleTopBuyBlockDays > 0)
        {
            var sellDate = latestSell.TradingDate.Kind == DateTimeKind.Utc
                ? latestSell.TradingDate
                : latestSell.TradingDate.ToUniversalTime();
            if ((utcTradingDate - sellDate).TotalDays < _bearMarketFilterOptions.PostCycleTopBuyBlockDays)
                return;
        }

        // Post-cycle-top SMA-ratio gate: after a DistributionSell, additionally block
        // re-entry while price > MaxMultiplier × SMA(PostCycleTopSmaRatioPeriodDays).
        // AthBreakoutBuy bypasses this gate — BTC at a new ATH is always above 2yr SMA (ADR-017).
        if (!isAthBreakoutBuy
            && _bearMarketFilterOptions.PostCycleTopBuyBlockEnabled
            && _bearMarketFilterOptions.PostCycleTopSmaRatioBlockEnabled
            && latestSell != null
            && latestSell.TradeAdvice == "DistributionSell")
        {
            var smaPeriod = _bearMarketFilterOptions.PostCycleTopSmaRatioPeriodDays;
            var fetchFrom = utcTradingDate.AddDays(-(smaPeriod + 5));
            var smaCandles = await _binanceKlineService.GetCandlesRangeAsync(
                "1d", fetchFrom, utcTradingDate, cancellationToken);
            var sorted = smaCandles.OrderBy(c => c.CloseTime).ToList();
            if (sorted.Count >= smaPeriod)
            {
                var window = sorted.TakeLast(smaPeriod);
                var sma = window.Average(c => c.Close);
                var currentClose = sorted[^1].Close;
                if (sma > 0 && currentClose / sma > _bearMarketFilterOptions.PostCycleTopSmaRatioMaxMultiplier)
                    return;
            }
        }

        // Post-cycle-top ATH-drawdown gate: after a DistributionSell, block re-entry
        // while price is still above MaxAthProximityPercent % of the running ATH.
        // AthBreakoutBuy bypasses this gate — price IS the ATH by definition (ADR-017).
        if (!isAthBreakoutBuy
            && _bearMarketFilterOptions.PostCycleTopBuyBlockEnabled
            && _bearMarketFilterOptions.PostCycleTopAthDrawdownBlockEnabled
            && latestSell != null
            && latestSell.TradeAdvice == "DistributionSell"
            && await _bearMarketFilterService.IsAthProximityTooHighForBuyAsync(
                _bearMarketFilterOptions.AthProximityBuyBlockInterval,
                utcTradingDate,
                _bearMarketFilterOptions.PostCycleTopAthDrawdownMaxAthProximityPercent,
                maxAthAgeDays: 0,
                cancellationToken))
        {
            return;
        }

        // ATH-Proximity gate (ADR-009): block new buy when close price is too near the ATH.
        // Applied after deep-cycle-low override so that PuellMultiple/AthDrawdown override
        // can still allow entry in deep bear troughs.
        // AthBreakoutBuy bypasses this gate — the entire signal IS that price broke the ATH (ADR-017).
        if (!isAthBreakoutBuy && _bearMarketFilterOptions.AthProximityBuyBlockEnabled
            && await _bearMarketFilterService.IsAthProximityTooHighForBuyAsync(
                _bearMarketFilterOptions.AthProximityBuyBlockInterval,
                utcTradingDate,
                _bearMarketFilterOptions.AthProximityBuyBlockMaxPercent,
                _bearMarketFilterOptions.AthProximityBuyBlockMaxAthAgeDays,
                cancellationToken))
        {
            return;
        }

        var buyActionLog = CreateBuyActionLog(deal, utcTradingDate, shouldSendMessage: true);
        await _tradingRepository.SaveBuyActionLog(buyActionLog, cancellationToken);
        await SendBuyMessage(deal, latestSell?.TradingDate);
    }

    private async Task<(DateTime TimestampUtc, decimal OpenPrice)?> TryResolveNextCandleOpenAsync(
        string interval,
        DateTime signalTimestampUtc,
        CancellationToken cancellationToken)
    {
        var candles = await _binanceKlineService.GetCandlesUpToAsync(
            interval,
            signalTimestampUtc.AddHours(48),
            cancellationToken);

        var next = candles
            .Where(c => c.OpenTime > signalTimestampUtc)
            .OrderBy(c => c.OpenTime)
            .FirstOrDefault();
        if (next == null || next.Open <= 0d)
        {
            return null;
        }

        return (DateTime.SpecifyKind(next.OpenTime, DateTimeKind.Utc), (decimal)next.Open);
    }

    private static bool HasOpenPosition(SellActionLog? latestSell, BuyActionLog? latestBuy)
    {
        return latestBuy != null && (latestSell == null || latestBuy.TradingDate > latestSell.TradingDate);
    }

    private static BuyActionLog CreateBuyActionLog(Deal deal, DateTime utcTradingDate, bool shouldSendMessage)
    {
        return new BuyActionLog
        {
            TradeAdvice = deal.TradeAdvice,
            TradingDate = utcTradingDate,
            Price = deal.Price,
            IsUrgent = shouldSendMessage,
            IsValidated = true,
            AlignedRanges = deal.AlignedRanges,
            TrendCountSum = deal.TrendCountSum
        };
    }

    private static SellActionLog CreateSellActionLog(Deal deal, DateTime utcTradingDate, BuyActionLog? buyActionLog, bool shouldSendMessage)
    {
        return new SellActionLog
        {
            TradeAdvice = deal.TradeAdvice,
            TradingDate = utcTradingDate,
            Price = deal.Price,
            BuyActionLog = buyActionLog,
            IsUrgent = shouldSendMessage,
            IsValidated = true,
            AlignedRanges = deal.AlignedRanges,
            TrendCountSum = deal.TrendCountSum
        };
    }

    private async Task SendSellMessage(Deal deal, string profit)
    {
        var now = deal.TradingDate.ToUtcDateTime().GetBerlinDateTime();
        var header = BuildSellHeader(deal, profit);
        var messageToSend = $"{now}: {header}\n{deal.DebugString}";
        await _telegramBotService.SendMessageAsync(messageToSend);
        await SaveActionLog(deal, "Sell");
    }

    private static string BuildSellHeader(Deal deal, string profit)
    {
        if (deal.DebugString.Contains("(trigger=TrailingStop)", StringComparison.Ordinal))
            return $"\U0001f6d1 TRAILING STOP | ${deal.Price:N0} | {profit}";
        if (deal.DebugString.Contains("(trigger=BreakevenStop)", StringComparison.Ordinal))
            return $"\u26a1 BREAKEVEN STOP | ${deal.Price:N0} | {profit}";
        return $"\U0001f4c9 {GetTranslatedAdvice(deal)} | ${deal.Price:N0} | {profit}";
    }

    private async Task SendBuyMessage(Deal deal, DateTime? latestSellDate)
    {
        var now = deal.TradingDate.ToUtcDateTime().GetBerlinDateTime();
        var header = $"\U0001f4c8 {GetTranslatedAdvice(deal)} | ${deal.Price:N0} | AR={deal.AlignedRanges} TC={deal.TrendCountSum} Score={deal.LinearBullishScore:N1}";
        var messageToSend = $"{now}: {header}\n{deal.DebugString}";
        await _telegramBotService.SendMessageAsync(messageToSend);
        await SaveActionLog(deal, "Buy");
    }

    private async Task SaveActionLog(Deal deal, string actionType)
    {
        await _actionLogService.SaveAsync(new ActionLog
        {
            Type = actionType,
            Price = deal.Price,
            TradingDate = deal.TradingDate,
            TradeAdvice = deal.TradeAdvice,
            IsLongTimeframe = LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe
        });
    }

    private async Task<bool> ShouldForceCycleTopSellAsync(
        string tradeAdvice,
        DateTime utcTradingDate,
        SellActionLog? latestSell,
        BuyActionLog? latestBuy,
        CancellationToken cancellationToken)
    {
        if (!_bearMarketFilterOptions.CycleTopSellZoneEnabled)
        {
            return false;
        }

        if (tradeAdvice.Contains("Sell", StringComparison.OrdinalIgnoreCase)
            || tradeAdvice.Contains("HardStop", StringComparison.OrdinalIgnoreCase)
            || tradeAdvice.Contains("TrailingStop", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!HasOpenPosition(latestSell, latestBuy))
        {
            return false;
        }

        if (!BitcoinConstants.IsWithinPostHalvingWindow(
                utcTradingDate,
                _bearMarketFilterOptions.CycleTopSellZoneMinMonthsAfterHalving,
                _bearMarketFilterOptions.CycleTopSellZoneMaxMonthsAfterHalving))
        {
            return false;
        }

        return await _bearMarketFilterService.IsCycleTopOvervaluedAsync(
            _bearMarketFilterOptions.CycleTopSellZoneInterval,
            utcTradingDate,
            _bearMarketFilterOptions.CycleTopSellZoneLookbackDays,
            _bearMarketFilterOptions.CycleTopSellZoneMinDrawupPercent,
            _bearMarketFilterOptions.CycleTopSellZoneMinAthProximityPercent,
            cancellationToken)
            && await IsCycleTopConfirmationSatisfiedAsync(utcTradingDate, cancellationToken);
    }

    /// <summary>
    /// Confirmation-Window check (ADR-008): when
    /// <see cref="BearMarketFilterOptions.CycleTopSellZoneConfirmationDays"/> is &gt; 0,
    /// require that the raw cycle-top zone was also active <c>N</c> days earlier.
    /// Additionally, when
    /// <see cref="BearMarketFilterOptions.CycleTopSellZoneConfirmationAthTolerancePercent"/>
    /// is &gt; 0, also require that the cycle ATH has not grown by more than the
    /// configured tolerance in between (skipped when tolerance &lt;= 0).
    /// Returns true when confirmation is disabled (N &lt;= 0).
    /// </summary>
    private async Task<bool> IsCycleTopConfirmationSatisfiedAsync(
        DateTime utcTradingDate,
        CancellationToken cancellationToken)
    {
        var confirmationDays = _bearMarketFilterOptions.CycleTopSellZoneConfirmationDays;
        if (confirmationDays <= 0)
        {
            return true;
        }

        var armedAt = utcTradingDate.AddDays(-confirmationDays);
        var rawAtArmed = await _bearMarketFilterService.IsCycleTopOvervaluedAsync(
            _bearMarketFilterOptions.CycleTopSellZoneInterval,
            armedAt,
            _bearMarketFilterOptions.CycleTopSellZoneLookbackDays,
            _bearMarketFilterOptions.CycleTopSellZoneMinDrawupPercent,
            _bearMarketFilterOptions.CycleTopSellZoneMinAthProximityPercent,
            cancellationToken);
        if (!rawAtArmed)
        {
            return false;
        }

        var tolerance = _bearMarketFilterOptions.CycleTopSellZoneConfirmationAthTolerancePercent / 100.0;
        if (tolerance <= 0)
        {
            // ATH-tolerance check disabled — armed-at-both alone is sufficient.
            return true;
        }

        var athNow = await _bearMarketFilterService.GetCycleAthAsync(
            _bearMarketFilterOptions.CycleTopSellZoneInterval,
            utcTradingDate,
            _bearMarketFilterOptions.CycleTopSellZoneLookbackDays,
            cancellationToken);
        var athArmed = await _bearMarketFilterService.GetCycleAthAsync(
            _bearMarketFilterOptions.CycleTopSellZoneInterval,
            armedAt,
            _bearMarketFilterOptions.CycleTopSellZoneLookbackDays,
            cancellationToken);
        if (athArmed <= 0)
        {
            return false;
        }

        return athNow <= athArmed * (1.0 + tolerance);
    }

    private static string GetTranslatedAdvice(Deal deal)
    {
        return TranslationService.Translate(deal.TradeAdvice);
    }
}
