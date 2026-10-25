# ADR-007 — DistributionTopFilter Disabled by Default (ADR-006 Parity)

**Status:** Accepted  
**Date:** 2026-05-08

## Context

ADR-006 established the optimal backtest parameters for the long-timeframe trading strategy.
The sweep results showed that disabling `DistributionTopFilter` (both Concept A and Concept B)
produces the best compound return (+1862%) over the 2017–2024 period.

The filter has two concepts:

- **Concept A – Momentum Brake**: Fires when the current cycle's gain is a small fraction of
  the previous completed cycle's gain (`currentGain / prevGain < MomentumBrakeMaxRatio`).
- **Concept B – Intermediate Trough**: Fires when the price formed a significant intra-cycle
  low and has since recovered near the cycle high.

With the default thresholds (`MomentumBrakeMaxRatio=0.25`, `IntermediateTroughMinDrawdownPercent=10`,
`IntermediateTroughRecoveryProximityPercent=5`), the filter caused two premature exits:

| Exit date   | Exit type        | Expected exit    | Impact                                      |
|-------------|------------------|------------------|---------------------------------------------|
| 2018-03-16  | DistributionSell | BreakevenStop    | Closed 40 days too early (Concept A)        |
| 2023-08-19  | DistributionSell | Sell 2024-09-20  | Missed +157% gain; extra re-entry in 2024   |

These two wrong exits reduce the compound return from **+1862%** to **+459.9%**.

## Decision

Disable both DistributionTopFilter concepts by setting default constants in
`DistributionTopThresholds`:

- `MomentumBrakeMaxRatio = 0.0` — disables Concept A (ratio can never be < 0.0)
- `IntermediateTroughMinDrawdownPercent = 999.0` — disables Concept B (no real drawdown exceeds 999%)
- `IntermediateTroughRecoveryProximityPercent = 999.0` — disables Concept B

This applies uniformly to **both** the backtest engine (`BacktestService`) and the real-time
signal processor (`BitcoinAdviceChangeService.ShouldOverrideToDistributionSellAsync`), which
both use `DistributionTopFilter.ShouldTriggerDistributionSell` with default params.

The `BacktestController` sweep endpoint still accepts these as query parameters, so individual
sweep runs can still test non-zero ratios if needed.

## Parity

Per project rules, replay and real-time trading must use one shared decision policy.
`BitcoinAdviceChangeService` calls `DistributionTopFilter.ShouldTriggerDistributionSell` without
custom params — it automatically picks up the new defaults. No code change required there.

## Consequences

- `DistributionSell` exits will no longer fire in real-time or backtest unless the caller
  explicitly passes non-default threshold values.
- The historical signal backfill will now produce the 7 ADR-006 trade entries without the two
  premature DistributionSell exits.
- Two unit tests in `DistributionTopFilterTests` were updated to pass explicit threshold params
  so they continue to test the function logic independent of the constants.
- The long-timeframe signal DB entries (LT=true, pre-2024-10-01) must be re-seeded after
  this change using `persist=true` with the ADR-006 optimal confluence params.
