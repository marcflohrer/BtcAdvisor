# ADR 006 – CycleTopZone sell gate refinements (Fixes A, B, C) + AthBreakoutBuy removal

**Status**: Accepted  
**Date**: 2025-07-15  
**Branch**: exp/macro-binance-f369db6

---

## Context

Root-cause analysis of the Oct 30 2024 premature sell ($72 k vs eventual $108 k ATH) revealed three independent design issues in the `macroOnly` backtest flow (`RunBacktestCoreAsync`):

1. **Fix A – SmaExitDebounce blocks CycleTop forced sells**  
   The `SmaExitDebounce` gate suppressed *all* `Sell`/`DistributionSell` advice while BTC was above the 200-day SMA (or had not been below it for N consecutive days). In `macroOnly` mode this is always true during a bull run, so the gate unconditionally blocked every `CycleTopZone` forced sell.

2. **Fix B – AthBreakoutBuy re-entry was permanently locked after one AthBreakoutBuy→CycleTopSell cycle**  
   When a position opened by `AthBreakoutBuy` was later closed by a `cycleTopForcedSell`, the block that records `simMacroSellCycleAth` was skipped (`if (!simLastEntryWasAthBreakout)`). This left `simMacroSellCycleAth = 0`, preventing any subsequent `AthBreakoutBuy` re-entry even when BTC went on to break new all-time highs.

3. **Fix C – MinAthProximityPercent threshold too low (98 %)**  
   The `CycleTopSellZoneMinAthProximityPercent` threshold of 98 % fired when BTC was at $72 827 against a cycle ATH of $73 794 (98.69 %). This was correct geometrically but premature: the running cycle ATH of $73.8 k was the March 2024 high; BTC ultimately reached $108 k in Q1 2025.  
   Raising the threshold to 99 % requires the price to be within 1 % of the running cycle ATH before the zone fires.

---

## Decisions

### Fix A – CycleTop forced sells bypass SmaExitDebounce

**Change**: Added `&& !cycleTopForcedSell` to the `SmaExitDebounce` gate condition in `RunBacktestCoreAsync`.

**Rationale**: CycleTop exits are macro-timing signals that are *expected* to fire at bull-market peaks, which by definition occur well above the 200-day SMA. The SMA filter is designed to prevent early exits during normal corrections; it should not block the cycle-top macro signal. `HardStopLoss` and `FireSell` were already exempt; `cycleTopForcedSell` is now added to that exemption list.

**Trade-offs**: None — the gate's purpose is to prevent selling into strength during a mid-bull correction, not to suppress the terminal cycle-top exit.

---

### Fix B – simMacroSellCycleAth updated unconditionally at every CycleTop forced sell

**Change**: Removed the `if (!simLastEntryWasAthBreakout)` guard from the `cycleTopForcedSell` sell-finalization block. `simMacroSellCycleAth` is now always recorded at the running `cycleAth` level when a `cycleTopForcedSell` closes a position. The now-dead `simLastEntryWasAthBreakout` variable was removed entirely.

**Rationale**: The original guard intended to prevent an infinite loop:  
`StrongBuy → CycleTopSell → AthBreakoutBuy → CycleTopSell → AthBreakoutBuy → …`  
However, the `passesBearish` filter (which requires price to drop a minimum percentage from the last accepted sell price before a new sell is accepted) already prevents tight-loop oscillation. The `if (!simLastEntryWasAthBreakout)` guard was therefore redundant and had the unintended side-effect of permanently locking out any further `AthBreakoutBuy` re-entries after a single `AthBreakoutBuy→CycleTopSell` sequence.

**Trade-offs**: The `passesBearish` anti-oscillation assumption must hold. If `passesBearish` is ever weakened or removed, this decision should be revisited.

---

### Fix C – CycleTopSellZoneMinAthProximityPercent raised from 98 % to 99 %

**Change**: `CycleTopSellZoneMinAthProximityPercent` in `appsettings.json` raised from `98.0` to `99.0`.

**Rationale**: A 98 % threshold fires when price is within 2 % of the running cycle ATH. In the 2024 cycle the running ATH was capped at the March 2024 high of ~$73.8 k; at 98.69 % proximity the zone triggered at $72.8 k — roughly $35 k below the eventual cycle top. Raising to 99 % requires a much tighter proximity to the actual ATH before treating the zone as "active," reducing false positives during mid-cycle ATH re-tests while still protecting against genuine distribution-zone peaks.

**Trade-offs**: A 99 % threshold may cause the zone to fire slightly later (by ~$700 at a $73 k ATH). This is acceptable; the purpose of the gate is to avoid acting at sub-ATH prices masquerading as cycle tops.

---

## Replay / Realtime parity

All three fixes are implemented exclusively in `BacktestService.RunBacktestCoreAsync`. The realtime trade advisor path is unaffected. No parity drift.

---

## Tests added / updated

| Test | Change |
|---|---|
| `RunBacktestAsync_MacroOnly_CycleTopForcedSell_BypassesSmaExitDebounce` | New — verifies Fix A |
| `RunBacktestAsync_MacroOnly_CycleTopZone_NoSell_WhenAthProximityBelowThreshold` | New — verifies Fix C with 99 % threshold |
| `RunBacktestAsync_MacroOnly_NoAthBreakoutReEntry_AfterDistSell` | Updated — was `AthBreakoutBuy_ReBuysAfterDistSell`; TotalBuys 2→1, verifies no re-entry |
| `RunBacktestAsync_MacroOnly_WaitsForDeepCycleLow_AfterMultipleCycleTopSells` | Updated — was `AthBreakoutBuy_AllowsReEntryAfterCycleTopSell`; verifies Wait-only behavior without new deep-cycle-low |

---

## Follow-up: AthBreakoutBuy removed from macroOnly (same branch)

### Context

After verifying Fixes A+B+C (6 buys, 5 sells, 260 % profit vs 148 % B&H), live inspection showed that `AthBreakoutBuy` entries occur at ATH+1 % prices — e.g., $69 792 after selling at $68 506, and $88 254 after selling at $72 415. These are momentum entries near the cycle top, not near the cycle low, which contradicts the stated macroOnly goal:

> "Beim oder nahe am Zyklus-Tief kaufen & Nahe am Zyklus Hoch verkaufen"

### Decision

**Remove `AthBreakoutBuy` entirely from macroOnly mode.** macroOnly now uses exclusively:
- `DeepCycleLow` → **StrongBuy** (buy near cycle trough)
- `CycleTopZone` → **DistributionSell** (sell near cycle peak)

All other timestamps → **Wait**.

### Changes

1. **macroOnly advice block**: Removed `isAthBreakout` variable and `AthBreakoutBuy` branch.
2. **`simMacroSellCycleAth`**: Dead variable removed (was only used to drive `isAthBreakout`).
3. **`cycleAthMap`**: Changed from named variable to `_` discard in tuple destructuring (was only used for `simMacroSellCycleAth` assignment in the sell finalization block).
4. **All buy gates with `!(macroOnlyMode && advice == "AthBreakoutBuy")`**: Changed to `!macroOnlyMode`, so that `DeepCycleLow` StrongBuy bypasses all post-cycleTop gates when in macroOnly mode.
5. **cycleTopForcedSell block**: Removed `simMacroSellCycleAth` assignment and the now-irrelevant comment about AthBreakoutBuy re-entry.

### Rationale

`AthBreakoutBuy` was designed as a momentum re-entry after the cycle-top confirmation. In practice it enters at prices that are far above the cycle low (e.g., $88 k after selling at $72 k, vs the original buy at $20 k). This adds three additional micro-trades during the 2024–2026 cycle that contribute minimal profit (3–11 % each) while introducing execution risk at high prices. The macroOnly mode is meant to be a pure cycle-timing overlay with exactly one buy per cycle trough and one sell per cycle peak.

### Trade-offs

- **Fewer round-trips per cycle**: Without `AthBreakoutBuy`, macroOnly holds cash from the cycle peak until the next genuine deep cycle low. In a bull run with multiple ATH extensions this means missed upside after each `DistributionSell`. This is the intended behavior.
- **Historical result**: With this change, macroOnly produces 1 buy ($20 390, 2022-06-18) and 1 sell ($68 506, 2024-03-05) for the 2022–2024 cycle, profit ≈ 236 %. The next DeepCycleLow will be required before any re-entry.

### Replay / Realtime parity

All changes are confined to `BacktestService.RunBacktestCoreAsync`. The realtime advisor is unaffected. No parity drift.

