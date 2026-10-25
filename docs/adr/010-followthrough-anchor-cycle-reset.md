# ADR 010 – Signal Follow-Through Anchor Resets per Position Cycle

**Status:** Accepted
**Date:** 2026-05-06
**Related:** ADR 008 (Same-Direction Signal Follow-Through Filter)

## Context

ADR 008 introduced `SignalFollowThroughFilter` to suppress noisy same-direction signal clusters:

- bullish signals require a price strictly above (or ≥ threshold% above) the **previous accepted bullish signal price**,
- bearish signals require a price strictly below the **previous accepted bearish signal price**.

The current thresholds are `BullishMinFollowThroughPercent = 0.0` and `BearishMinFollowThroughPercent = 0.0`, i.e. monotonic anchors.

Both `BacktestService` and `TradeOpportunityService` (realtime) anchored against the **most recent accepted signal globally**, with no awareness of position cycles.

### Observed defect

Diagnostics over 2017→2026 BTC (default-off Slice 1–3) revealed:

- 2042 raw buys → 807 passed the regime gate → only 8 actually executed.
- **658 of 807 (≈81.5 %) regime-passed buys were blocked by `SignalFollowThroughFilter.PassesBullish`.**
- Mechanism: after the very first accepted buy at e.g. ~$19k (Dec 2017), every subsequent buy at any price below $19k was rejected — even though that earlier position had long since been closed by a HardStopLoss.
- Net effect: the strategy could not enter any new long-cycle dip-buys for years; backtest cumulative return collapsed to −164.48 % vs. +1678 % buy-and-hold over the same window with a 0 % win rate.

The defect contradicts ADR 008's stated intent ("prevent **same-direction signal clusters**"): once a position is closed, the next buy is the **start of a new cluster**, not a continuation of the prior one.

## Decision

Reset the opposing follow-through anchor at every position-cycle transition:

- **`BacktestService`** sets `lastAcceptedBuySignalPrice = null` after every position close (regular Sell, FireSell, DistributionSell, HardStopLoss, future AtrStop) in both the diagnostics loop and the main backtest loop. Symmetrically `lastAcceptedSellSignalPrice = null` after every accepted Buy.
- **`TradeOpportunityService`** passes `previousAcceptedPrice: null` to `PassesBullish` / `PassesBearish` at the `HandleBuy` / `HandleSell` call sites. Realtime never accepts two same-direction signals within a single cycle (`HasOpenPosition` already gates that), so cross-cycle anchoring would always be wrong; passing null disables the cross-cycle gate entirely while leaving the within-sequence helper `FilterOrdered` (used by the insights sweep) untouched.

Replay and realtime stay parity-preserving: both sides treat the anchor as **phase-scoped**, not process-scoped.

## Consequences

- Cross-cycle dip-buys and rally-sells are no longer silently dropped.
- The `BullishMinFollowThroughPercent` / `BearishMinFollowThroughPercent` knobs continue to gate within a cluster (relevant only to the insights sweep / `FilterOrdered`); their semantics do not change for that path.
- Two prior tests that asserted the buggy cross-cycle behavior were inverted (`RunBacktestAsync_BuyBelowPreviousAcceptedBuy_*`, `RunBacktestAsync_SellAbovePreviousAcceptedSell_*`, `TradeOpportunity_WhenBuyPriceFallsBelowPreviousAcceptedBuy_*`, `TradeOpportunity_WhenSellPriceRisesAbovePreviousAcceptedSell_*`).
- Re-measurement of cumulative profit 2017→now is required and recorded alongside this ADR.

## Measured impact (2017-01-01 → 2026-01-01, BTC, default-off Slice 1–3)

| Metric | Before fix | After fix |
|---|---|---|
| Buys / Sells | 8 / 8 | 52 / 52 |
| Win rate | 0.0 % | 28.8 % |
| Cumulative return | −164.48 % | −401.39 % |
| Buy & Hold | +1678 % | +1940 % |
| Max gain / loss per trade | — | +24.71 % / −42.55 % |

The fix unblocks signal flow (8 → 52 trades, 0 % → 28.8 % win rate) but the underlying strategy remains unprofitable: compounding losses in 2018/2019/2021/2025 push cumulative deeper red. This is now a calibration problem (HardStopLoss too lax, Slice 1–3 disabled by default, LTF thresholds calibrated only over 2023–2026) rather than a structural defect, and is in scope of subsequent ADRs.
