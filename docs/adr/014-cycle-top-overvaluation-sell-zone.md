# 014: Cycle-Top Overvaluation Sell-Zone Boost

Date: 2026-05-09
Status: Accepted

## Context

The deep-cycle-low mode improved entry timing in severe bear phases, but exit timing can still lag when the market moves into overvalued late-cycle zones.

Current behavior before this ADR:

- Buy side has a deep-cycle-low override in bear regime.
- Sell side depends mainly on existing sell advice and risk exits.
- Wait phases during expensive late-cycle ranges can delay profit-taking.

## Decision

Introduce a separate cycle-top overvaluation model that can upgrade non-sell advice to `DistributionSell` when all of the following are true:

1. A position is open.
2. `CycleTopSellZoneEnabled` is true.
3. The latest candle high is near cycle ATH within a rolling lookback window.
4. The latest candle high is far enough above cycle low (drawup threshold).

The model is implemented in `IBearMarketFilterService` / `BearMarketFilterService` via:

- `IsCycleTopOvervaluedAsync(interval, asOfUtc, lookbackDays, minDrawupFromCycleLowPercent, minAthProximityPercent, ct)`

Sell-zone defaults:

- `CycleTopSellZoneEnabled = false`
- `CycleTopSellZoneInterval = 1d`
- `CycleTopSellZoneLookbackDays = 1460`
- `CycleTopSellZoneMinDrawupPercent = 350`
- `CycleTopSellZoneMinAthProximityPercent = 90`

## Replay/Realtime Parity

The override is applied in both:

- `TradeOpportunityService` (realtime)
- `BacktestService` (replay/backtest and diagnostics)

This keeps entry/exit decision policy aligned across execution modes.

## Consequences

Positive:

- Makes sell execution more likely in expensive late-cycle zones.
- Preserves existing emergency exits and deep-cycle-low suppression semantics.
- Keeps behavior configurable and disabled by default.

Trade-off:

- More strategy complexity and more configuration parameters.
- If thresholds are too aggressive, exits can become too early.

## Follow-up

Run 2017-2026 comparison matrix for:

1. Baseline without cycle-top boost.
2. Deep-cycle-low only.
3. Deep-cycle-low + cycle-top boost.

Tune lookback/drawup/proximity thresholds based on yearly realized PnL and drawdown stability.
