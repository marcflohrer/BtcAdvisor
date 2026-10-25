namespace BtcAdvisor.Services;

/// <summary>
/// Result of a stop-loss evaluation. When <see cref="Triggered"/> is true, <see cref="ExitPrice"/> contains the
/// modeled fill price (threshold × (1 − slippage)) used for trade recording.
/// </summary>
public readonly record struct StopLossEvaluation(bool Triggered, decimal ExitPrice);

public interface IBacktestRiskControlService
{
    bool ShouldTriggerHardStopLoss(decimal entryPrice, decimal currentPrice, double thresholdPercent);

    /// <summary>
    /// Triggers when the current price has fallen at or below entryPrice − atrMultiplier · atrValue.
    /// Returns false when atrValue is null/non-positive (insufficient data → fall back to HardStop only).
    /// </summary>
    bool ShouldTriggerAtrStop(decimal entryPrice, decimal currentPrice, double? atrValue, double atrMultiplier);

    /// <summary>
    /// Realistic hard-stop evaluation: triggers when intra-bar low (or current price when bar low is unknown)
    /// reaches the threshold price. Returns the modeled fill price applying <paramref name="slippagePercent"/>.
    /// </summary>
    StopLossEvaluation EvaluateHardStop(decimal entryPrice, decimal currentPrice, decimal? barLow, double thresholdPercent, double slippagePercent);

    /// <summary>
    /// Realistic ATR-stop evaluation: triggers when intra-bar low (or current price when bar low is unknown)
    /// reaches the ATR-based stop level. Returns the modeled fill price applying <paramref name="slippagePercent"/>.
    /// </summary>
    StopLossEvaluation EvaluateAtrStop(decimal entryPrice, decimal currentPrice, decimal? barLow, double? atrValue, double atrMultiplier, double slippagePercent);

    /// <summary>
    /// Evaluates a trailing stop. Only activates once the high-water-mark gain from entry
    /// has exceeded <paramref name="activationPercent"/>. Triggers when the current price
    /// has fallen more than <paramref name="trailingPercent"/> below the high-water mark.
    /// Fill is modeled at the trail level minus slippage.
    /// </summary>
    StopLossEvaluation EvaluateTrailingStop(
        decimal entryPrice,
        decimal currentPrice,
        decimal highWaterMark,
        double activationPercent,
        double trailingPercent,
        double slippagePercent);
}
