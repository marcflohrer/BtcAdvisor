namespace BtcAdvisor.Services;

public class BacktestRiskControlService : IBacktestRiskControlService
{
    public bool ShouldTriggerHardStopLoss(decimal entryPrice, decimal currentPrice, double thresholdPercent)
    {
        if (entryPrice <= 0)
        {
            return false;
        }

        var changePercent = ((double)(currentPrice - entryPrice) / (double)entryPrice) * 100d;
        return changePercent <= thresholdPercent;
    }

    public bool ShouldTriggerAtrStop(decimal entryPrice, decimal currentPrice, double? atrValue, double atrMultiplier)
    {
        if (entryPrice <= 0)
        {
            return false;
        }
        if (!atrValue.HasValue || atrValue.Value <= 0d)
        {
            return false;
        }
        if (atrMultiplier <= 0d)
        {
            return false;
        }

        var stopLevel = (double)entryPrice - (atrMultiplier * atrValue.Value);
        return (double)currentPrice <= stopLevel;
    }

    public StopLossEvaluation EvaluateHardStop(decimal entryPrice, decimal currentPrice, decimal? barLow, double thresholdPercent, double slippagePercent)
    {
        if (entryPrice <= 0)
        {
            return new StopLossEvaluation(false, 0m);
        }

        var thresholdPrice = entryPrice * (decimal)(1d + thresholdPercent / 100d);

        // Trigger: close must cross threshold (same as legacy — avoids wick-whipsaw).
        // Fill: at threshold price × (1 − slippage) — realistic stop-market fill.
        if (currentPrice > thresholdPrice)
        {
            return new StopLossEvaluation(false, 0m);
        }

        var slippageFactor = (decimal)Math.Max(0d, 1d - slippagePercent / 100d);
        var fill = thresholdPrice * slippageFactor;
        return new StopLossEvaluation(true, fill);
    }

    public StopLossEvaluation EvaluateAtrStop(decimal entryPrice, decimal currentPrice, decimal? barLow, double? atrValue, double atrMultiplier, double slippagePercent)
    {
        if (entryPrice <= 0 || !atrValue.HasValue || atrValue.Value <= 0d || atrMultiplier <= 0d)
        {
            return new StopLossEvaluation(false, 0m);
        }

        var stopLevel = (decimal)((double)entryPrice - atrMultiplier * atrValue.Value);
        if (stopLevel <= 0)
        {
            return new StopLossEvaluation(false, 0m);
        }

        // Trigger: close must reach stop level (avoids wick-whipsaw).
        // Fill: at stop level × (1 − slippage).
        if (currentPrice > stopLevel)
        {
            return new StopLossEvaluation(false, 0m);
        }

        var slippageFactor = (decimal)Math.Max(0d, 1d - slippagePercent / 100d);
        var fill = stopLevel * slippageFactor;
        return new StopLossEvaluation(true, fill);
    }

    public StopLossEvaluation EvaluateTrailingStop(
        decimal entryPrice,
        decimal currentPrice,
        decimal highWaterMark,
        double activationPercent,
        double trailingPercent,
        double slippagePercent)
    {
        if (entryPrice <= 0 || highWaterMark <= 0)
        {
            return new StopLossEvaluation(false, 0m);
        }

        // Only activate once the high-water-mark gain from entry has exceeded the threshold.
        var hwmGainPercent = ((double)(highWaterMark - entryPrice) / (double)entryPrice) * 100d;
        if (hwmGainPercent < activationPercent)
        {
            return new StopLossEvaluation(false, 0m);
        }

        var trailLevel = highWaterMark * (decimal)(1d - trailingPercent / 100d);
        if (currentPrice > trailLevel)
        {
            return new StopLossEvaluation(false, 0m);
        }

        var slippageFactor = (decimal)Math.Max(0d, 1d - slippagePercent / 100d);
        var fill = trailLevel * slippageFactor;
        return new StopLossEvaluation(true, fill);
    }
}
