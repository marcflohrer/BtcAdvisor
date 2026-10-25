using Microsoft.Extensions.Options;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public class BitcoinAdviceChangeService(
    ITradingRepository tradingRepository,
    ITradeOpportunityService tradeOpportunityService,
    IBacktestRiskControlService riskControlService,
    IOptions<RiskControlOptions> riskControlOptions,
    IAtrLookupService atrLookupService,
    IBinanceKlineService binanceKlineService)
{
    public async Task SendChangeAlertsToBot(DateTime dateTime, CancellationToken cancellationToken)
    {
        var (_, latestBuyPrice) = await tradingRepository.FindLatestAction(dateTime);
        var latestAnySellPrice = await tradingRepository.FindLatestSellAction(dateTime);

        // Hard Stop-Loss check: fires before normal signal evaluation
        if (latestBuyPrice != null
            && (riskControlOptions.Value.EnableHardStopLoss || riskControlOptions.Value.EnableAtrStopLoss)
            && (latestAnySellPrice == null || latestBuyPrice.TradingDate > latestAnySellPrice.TradingDate))
        {
            var currentPriceForStopLoss = await GetCurrentBitcoinPriceAsync(dateTime, cancellationToken);
            if (currentPriceForStopLoss.HasValue)
            {
                var atrEval = new StopLossEvaluation(false, 0m);
                double? atrValue = null;
                if (riskControlOptions.Value.EnableAtrStopLoss)
                {
                    atrValue = await atrLookupService.GetAtrAsync(
                        riskControlOptions.Value.AtrInterval,
                        dateTime,
                        riskControlOptions.Value.AtrPeriod,
                        cancellationToken);
                    atrEval = riskControlService.EvaluateAtrStop(
                        latestBuyPrice.Price,
                        currentPriceForStopLoss.Value,
                        barLow: null,
                        atrValue,
                        riskControlOptions.Value.AtrMultiplier,
                        riskControlOptions.Value.StopLossSlippagePercent);
                }

                var hardEval = riskControlOptions.Value.EnableHardStopLoss
                    ? riskControlService.EvaluateHardStop(
                        latestBuyPrice.Price,
                        currentPriceForStopLoss.Value,
                        barLow: null,
                        riskControlOptions.Value.HardStopLossPercent,
                        riskControlOptions.Value.StopLossSlippagePercent)
                    : new StopLossEvaluation(false, 0m);

                if (atrEval.Triggered || hardEval.Triggered)
                {
                    var trigger = atrEval.Triggered ? "AtrStop" : "HardStopLoss";
                    var exitPrice = atrEval.Triggered && hardEval.Triggered
                        ? Math.Max(atrEval.ExitPrice, hardEval.ExitPrice)
                        : (atrEval.Triggered ? atrEval.ExitPrice : hardEval.ExitPrice);
                    var debug = atrEval.Triggered
                        ? $"AtrStop triggered: entry={latestBuyPrice.Price:C} current={currentPriceForStopLoss.Value:C} fill={exitPrice:C} atr={atrValue} k={riskControlOptions.Value.AtrMultiplier} slip={riskControlOptions.Value.StopLossSlippagePercent}%"
                        : $"HardStopLoss triggered: entry={latestBuyPrice.Price:C} current={currentPriceForStopLoss.Value:C} fill={exitPrice:C} threshold={riskControlOptions.Value.HardStopLossPercent}% slip={riskControlOptions.Value.StopLossSlippagePercent}%";
                    var stopLossDeal = new Deal
                    {
                        DecisionSource = DecisionSource.HardStopLoss,
                        TradeAdvice = "HardStopLoss",
                        Price = exitPrice,
                        TradingDate = dateTime,
                        DebugString = debug + $" (trigger={trigger})"
                    };
                    await tradeOpportunityService.TradeOpportunity(stopLossDeal, cancellationToken);
                    return;
                }
            }
        }

        // Trailing Stop and Breakeven Stop — evaluated after hard/ATR stops.
        // Uses daily candles since entry (already in-memory cache, no API call).
        if (latestBuyPrice != null
            && HasOpenPosition(latestBuyPrice, latestAnySellPrice)
            && (riskControlOptions.Value.EnableTrailingStop || riskControlOptions.Value.BreakevenTriggerPercent > 0))
        {
            var currentPrice = await GetCurrentBitcoinPriceAsync(dateTime, cancellationToken);
            if (currentPrice.HasValue)
            {
                var entryDate = latestBuyPrice.TradingDate.Kind == DateTimeKind.Utc
                    ? latestBuyPrice.TradingDate
                    : latestBuyPrice.TradingDate.ToUniversalTime();
                var candles1d = await binanceKlineService.GetCandlesRangeAsync("1d", entryDate, dateTime, cancellationToken);

                var trailEval = new StopLossEvaluation(false, 0m);
                decimal highWaterMark = 0m;
                if (riskControlOptions.Value.EnableTrailingStop && candles1d.Count > 0)
                {
                    highWaterMark = (decimal)candles1d.Max(c => c.High);
                    trailEval = riskControlService.EvaluateTrailingStop(
                        latestBuyPrice.Price,
                        currentPrice.Value,
                        highWaterMark,
                        riskControlOptions.Value.TrailingStopActivationPercent,
                        riskControlOptions.Value.TrailingStopPercent,
                        riskControlOptions.Value.StopLossSlippagePercent);
                }

                var breakevenEval = new StopLossEvaluation(false, 0m);
                var breakevenTriggerPct = riskControlOptions.Value.BreakevenTriggerPercent;
                if (breakevenTriggerPct > 0 && candles1d.Count > 0)
                {
                    // Activation: any daily close must have reached the threshold (close-price-based, consistent with backtest).
                    var breakevenActive = candles1d.Any(c =>
                        (double)((decimal)c.Close / latestBuyPrice.Price - 1m) * 100.0 >= breakevenTriggerPct);
                    if (breakevenActive)
                    {
                        // Exit when close price returns to or below entry (threshold 0%).
                        breakevenEval = riskControlService.EvaluateHardStop(
                            latestBuyPrice.Price,
                            currentPrice.Value,
                            barLow: null,
                            thresholdPercent: 0.0,
                            riskControlOptions.Value.StopLossSlippagePercent);
                    }
                }

                if (trailEval.Triggered || breakevenEval.Triggered)
                {
                    var isTrailing = trailEval.Triggered
                        && (!breakevenEval.Triggered || trailEval.ExitPrice >= breakevenEval.ExitPrice);
                    var exitPrice = trailEval.Triggered && breakevenEval.Triggered
                        ? Math.Max(trailEval.ExitPrice, breakevenEval.ExitPrice)
                        : (trailEval.Triggered ? trailEval.ExitPrice : breakevenEval.ExitPrice);
                    var triggerLabel = isTrailing ? "TrailingStop" : "BreakevenStop";
                    var debugMsg = isTrailing
                        ? $"TrailingStop triggered: entry={latestBuyPrice.Price:C} hwm={highWaterMark:C} current={currentPrice.Value:C} fill={exitPrice:C} trail={riskControlOptions.Value.TrailingStopPercent}% slip={riskControlOptions.Value.StopLossSlippagePercent}%"
                        : $"BreakevenStop triggered: entry={latestBuyPrice.Price:C} current={currentPrice.Value:C} fill={exitPrice:C} threshold={breakevenTriggerPct}% slip={riskControlOptions.Value.StopLossSlippagePercent}%";
                    var softStopDeal = new Deal
                    {
                        DecisionSource = DecisionSource.HardStopLoss,
                        TradeAdvice = "HardStopLoss",
                        Price = exitPrice,
                        TradingDate = dateTime,
                        DebugString = debugMsg + $" (trigger={triggerLabel})"
                    };
                    await tradeOpportunityService.TradeOpportunity(softStopDeal, cancellationToken);
                    return;
                }
            }
        }

        var price = await GetCurrentBitcoinPriceAsync(dateTime, cancellationToken);
        if (price is null)
        {
            Console.WriteLine($"{dateTime:o} No current price available.");
            return;
        }

        var deal = new Deal
        {
            TradeAdvice = "MacroOnly",
            Price = price.Value,
            TradingDate = dateTime
        };

        await tradeOpportunityService.TradeOpportunity(deal, cancellationToken);
    }

    private static bool HasOpenPosition(BuyActionLog latestBuy, SellActionLog? latestSell)
        => latestSell == null || latestBuy.TradingDate > latestSell.TradingDate;

    private async Task<decimal?> GetCurrentBitcoinPriceAsync(DateTime dateTime, CancellationToken cancellationToken)
    {
        var candles = await binanceKlineService.GetCandlesUpToAsync("1h", dateTime, cancellationToken);
        var last = candles.LastOrDefault();
        if (last is null || last.Close <= 0) return null;
        return (decimal)last.Close;
    }
}
