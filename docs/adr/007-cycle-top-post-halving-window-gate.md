# ADR 007 — Cycle-Top Post-Halving Window Gate

Status: Accepted
Date: 2026-05-15
Supersedes: —
Superseded by: —

## Context

In the macroOnly backtest window 2022-06 → 2026-05, the `DistributionSell`
(CycleTopZone) trigger fired on **2024-03-14 around the $73 k local ATH**.
That local ATH was a *false* cycle top — it occurred **one month before**
the 2024-04-19 halving and 13 months before the actual run to $108 k+ in
Q1/Q2 2025. The premature exit cost ~210 percentage points of profit
(235 % vs 446 % in the same backtest).

Historically, Bitcoin cycle tops have clustered **12–18 months after each
halving**:

| Halving        | Cycle top      | Δ months |
|----------------|----------------|---------:|
| 2012-11-28     | 2013-12-04     | ~12      |
| 2016-07-09     | 2017-12-17     | ~17      |
| 2020-05-11     | 2021-11-10     | ~18      |
| 2024-04-19     | (open)         | —        |

A drawup-and-proximity gate alone has no way to distinguish a "late-cycle
genuine top" from a "pre-halving fakeout ATH". The signal needs a temporal
prior tied to the halving cycle.

## Decision

Add a **post-halving window gate** to the CycleTopZone evaluation:

> A `DistributionSell` via `CycleTopZone` may only fire when the current
> UTC timestamp falls within `[MinMonthsAfterHalving, MaxMonthsAfterHalving]`
> of the most recent prior halving.

Implementation:

1. **`BitcoinConstants.Halvings`** holds the four known halving timestamps
   (UTC). `BitcoinConstants.IsWithinPostHalvingWindow(asOfUtc, min, max)`
   returns `true` when no prior halving exists (**fail-open** for the
   pre-2012 era) or when the elapsed months since the most recent prior
   halving fall within `[min, max]`. Setting `min = max = 0` disables the
   gate entirely.
2. **`BearMarketFilterOptions`** exposes
   `CycleTopSellZoneMinMonthsAfterHalving` (default **12**) and
   `CycleTopSellZoneMaxMonthsAfterHalving` (default **30**).
3. **`BacktestService.PrecomputeCycleTopMapAsync`** ANDs the gate into the
   per-date zone map alongside drawup and ATH proximity.
4. **`TradeOpportunityService`** applies the same gate at both realtime
   call sites (entry-time `CycleTopZone` check and
   `ShouldForceCycleTopSellAsync`) so replay and realtime stay in parity.

Default upper bound is **30 months** (not the historical max of 18) to
provide buffer for cycle extension as halving rewards diminish; the gate
is intentionally permissive on the upper end and strict on the lower end.

## Consequences

Positive:

- The March 2024 pre-halving false-top is suppressed; the strategy holds
  through the actual 12–18-month post-halving cycle top.
- Live backtest 2022-06 → 2026-05 improved from **+235.98 %** to
  **+445.99 %** (single buy/sell pair) and now beats buy-and-hold
  (+148.74 %) by ~3×.
- Replay and realtime parity is preserved: both paths consult the same
  helper with the same option values.

Negative / risks:

- The halving date list is **hardcoded** and must be extended manually
  for the next halving (estimated ~April 2028).
- The gate is purely temporal — it does not know about halvings that
  have not yet happened. After the *next* halving (2028), the
  `[12, 30]` window will shift automatically because
  `IsWithinPostHalvingWindow` always anchors to the most recent prior
  halving.
- Bitcoin cycle behavior may decouple from the halving rhythm in future
  cycles. If so, this gate becomes a drag and the bounds should be
  loosened or the gate retired.

Mitigations:

- The gate disables cleanly via `Min=0, Max=0`. All existing
  non-halving-focused tests opt out this way to keep their synthetic
  date setups working.
- Unit tests (`BitcoinHalvingWindowGateTests`) cover all four halvings
  plus the historical cycle tops to catch any regression of the helper
  itself.

## Validation

- `BitcoinHalvingWindowGateTests`: 9 unit tests covering pre-halving
  fail-open, both-bounds-zero disable, March 2024 block, post-halving
  ramp-in, post-window block, asymmetric bounds, and the three
  historical cycle tops.
- `BacktestServiceTests.RunBacktestAsync_MacroOnly_HalvingWindowGate_BlocksZone_OutsideWindow`:
  integration coverage proving the gate suppresses an otherwise valid
  zone trigger when outside the window.
- Full test suite: 425 passed, 7 skipped, 0 failed.
- Live backtest 2022-06 → 2026-05 verified the suppression of the
  2024-03 false-top.

## Configuration

`appsettings.json`:

```json
"BearMarketFilter": {
  "CycleTopSellZoneMinMonthsAfterHalving": 12,
  "CycleTopSellZoneMaxMonthsAfterHalving": 30
}
```
