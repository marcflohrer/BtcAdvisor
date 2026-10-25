# ADR 009 - Bear Regime Linear Threshold Grid

**Status:** Accepted
**Date:** 2026-05-05

## Context

Walk-forward runs showed unstable out-of-sample deltas even after confluence threshold tuning.
The existing bear-regime guard allows bullish entries only when linear bullish score is above a fixed constant.
That fixed constant is strategy-critical but was not part of sweep/walk-forward calibration.

Per product constraints, replay and realtime must use one shared decision policy.

## Decision

Make the bear-regime minimum linear bullish score a first-class calibration parameter in backtest tooling:

- add a configurable threshold to market-regime decision filtering,
- keep realtime/default behavior unchanged via existing default constant,
- expose this threshold in:
  - backtest endpoint,
  - diagnostics endpoint,
  - sweep endpoint (as grid),
  - walk-forward endpoint (as grid),
- include the chosen threshold in sweep rows and walk-forward fold outputs.

## Consequences

- Strategy tuning can now optimize both confluence gates and bear-regime linear entry strictness.
- Replay and realtime remain behaviorally aligned at default settings.
- Parameter choice becomes auditable in sweep and fold results.
- Search space increases by one dimension and should be kept controlled for runtime.
