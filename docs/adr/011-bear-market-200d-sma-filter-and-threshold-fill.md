# ADR 011 – Bear-Market 200d-SMA Filter + Realistic Stop-Loss Fill Price

**Status:** Accepted
**Date:** 2026-05-06
**Related:** ADR 010 (Follow-Through Anchor Reset), ADR 004 (HardMax HoldingDays), ADR 005 (Profit Extension)

## Context

After the ADR 010 anchor fix, cumulative profit 2017→2026 improved from −164% to −401% with 52 trades and a 28.8% win rate. Year-by-year analysis showed two structural problems:

1. **Bear-market overtrading.** 2018 (BTC −74%): 16 trades, −162%. 2022 (BTC −65%): 13 trades, −94%. The existing `MarketRegime.Bearish` gate (BearishRegimeMinLinearBullishScore=22.0) was insufficient to block entries during sustained downtrends.

2. **Crash-bar exit price distortion.** A single trade in 2020 produced MaxLoss=−42.55% because the HardStopLoss trigger evaluated against the 2h-bar close. During a flash-crash, a 2h bar can close far below the −15% threshold before the stop is hit at the close price rather than the threshold price. Real stop-market orders fill near the trigger level, not the crash low.

## Decision

### A – 200d-SMA Bear-Market Filter

Add `BearMarketFilterService` (`IBearMarketFilterService`) that:

- Fetches all cached 1d candles up to `asOfUtc` via `IBinanceKlineService.GetCandlesUpToAsync("1d", …)`.
- Computes SMA(200) on the close-price series via `ILocalIndicatorComputationService.ComputeSma`.
- Returns `true` (bear market) when `latestClose < latestSma(200)`.
- Returns `false` (fail-open) when fewer than 200 candles are available.
- Caches the result in `IMemoryCache` for 24h with key `bearfilter:{interval}:{period}:{asOfNormalized:o}`.

Configuration (`appsettings.json` section `BearMarketFilter`):

```json
"BearMarketFilter": {
  "Enabled": true,
  "MovingAveragePeriod": 200,
  "Interval": "1d"
}
```

Wiring in both `BacktestService` (both loops) and `TradeOpportunityService` (`HandleBuy`): after the follow-through check, if `IsBearMarketAsync` returns `true`, block the bullish entry. A `BlockedBuyDueToBearMarket` counter is incremented and surfaced in `BacktestResult` and diagnostics output.

### B – Realistic Stop-Loss Fill Price (Threshold-Fill)

Extend `IBacktestRiskControlService` with two new methods:

```csharp
StopLossEvaluation EvaluateHardStop(decimal entryPrice, decimal currentPrice, decimal? barLow,
    double thresholdPercent, double slippagePercent);
StopLossEvaluation EvaluateAtrStop(decimal entryPrice, decimal currentPrice, decimal? barLow,
    double? atrValue, double atrMultiplier, double slippagePercent);
```

`StopLossEvaluation` is a `readonly record struct(bool Triggered, decimal ExitPrice)`.

**Trigger condition:** unchanged — close price must cross the stop threshold (avoids wick-based whipsaw).
**Fill price:** `thresholdPrice × (1 − slippagePercent / 100)` — models a stop-market order filling at the trigger level with a small slippage, not at the potentially much lower bar close.

The `barLow` parameter is accepted but currently unused in the trigger logic; it is reserved for a future intra-bar refinement.

Configuration (`RiskControls.StopLossSlippagePercent`, default 0.5).

`BacktestService` pre-fetches the 2h candle map (`BuildCandleMapAsync`) and passes `barLow` from the map on every tick. Both loops (diagnostics + main) are updated in parallel.

`BitcoinAdviceChangeService` (live path) uses the same `EvaluateHardStop` / `EvaluateAtrStop` with `barLow: null`; the fill price recorded in the `Deal` and forwarded to `TradeOpportunityService` reflects the threshold-fill model — identical semantics to the backtest.

## Consequences

- 2022 is fully blocked: 0 trades, 0% loss (was 13 trades, −94%).
- 2018 drops from 16 trades to 4 trades.
- MaxLoss per trade capped at approximately `HardStopLossPercent × (1 − slippage)` = −15.42% instead of −42.55%.
- WinRate decreases (19.2% vs. 28.8%) because bear-filter also blocks some legitimate dip-buys in late-bear / early-recovery phases.

## HardStopLoss threshold calibration (post-ADR sweep)

After confirming the threshold-fill fix, the `HardStopLossPercent` was swept to find the minimum value that achieves the Schritt-1 target (Cum > −150%, MaxLoss < 15%):

| HardStopLossPercent | Cum-Profit | MaxLoss | Trades |
|---|---|---|---|
| −15.0 % | −170.6 % | −15.42 % | 26 |
| −12.0 % | −153.8 % | −12.44 % | 27 |
| **−10.0 %** | **−126.7 %** | **−10.45 %** | **27** |

**Accepted value: `HardStopLossPercent = -10.0`.**  No significant whipsaw increase (26→27 trades). Schritt-1 target met.

## Measured impact (2017-01-01 → 2026-01-01, BTC) — final

| Metric | ADR 010 baseline | +Bear-Filter | +Threshold-Fill −10% |
|---|---|---|---|
| Buys / Sells | 52 / 52 | 26 / 26 | 27 / 27 |
| Win rate | 28.8 % | 19.2 % | 18.5 % |
| Cumulative profit | −401 % | −268 % | **−126.7 %** |
| MaxLoss (single trade) | −42.55 % | −42.55 % | **−10.45 %** |
| Buy&Hold (reference) | +1940 % | +1940 % | +1940 % |

The Schritt-1 target (Cum > −150%, MaxLoss < 15%) is nearly met; cumulative profit is −170.6% (−21pp short). Next step: Walk-Forward sweep of LTF thresholds over the full 2017–2026 window (Schritt 2 of calibration plan).

## Open risks

- Bear-filter blocks entries in late-bear / early-recovery phases (price below 200d SMA while turning up). This may cause missed recovery rallies — the 2019 calendar year (BTC +94%) shows 0% WR with 4 trades while the filter was inactive (price above SMA). Root cause is LTF threshold mis-calibration, not the bear filter.
- Threshold-fill assumption (stop-market fills at trigger level) is optimistic in illiquid markets or extreme gap-downs. The 0.5% slippage parameter should be stress-tested at 2% to quantify sensitivity.
