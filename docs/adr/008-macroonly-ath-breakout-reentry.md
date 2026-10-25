# ADR-008: macroOnly ATH-Breakout Re-Entry after DistributionSell

**Status**: Accepted  
**Date**: 2025-07-17

---

## Context

After Phase 4 (`CycleTopSellZoneMinAthProximityPercent = 98 %`) the macroOnly strategy
exited Trade 1 in November 2020 at ~\$19 100 (98 % of the 2017 ATH ≈ \$19 891).
BTC then broke out to \$68 789 by November 2021 (+260 %).

The strategy had no re-entry signal until `DeepCycleLowBuy` fired at \$20 390 in June 2022
— **after** the \$69 k top and the subsequent \$15 k crash.  This was the single largest
missed move in the 2017–2026 dataset.

---

## Decision

After a macroOnly `DistributionSell`, fire an **`AthBreakoutBuy`** signal when:

```C#
!simHasOpenPosition
  && simMacroSellCycleAth > 0
  && (double)price > simMacroSellCycleAth * 1.01
```

`simMacroSellCycleAth` is the rolling `cycleAth` captured from `cycleAthMap` at the moment
of the `DistributionSell`.  It is cleared when any buy executes.

The **1 % breakout buffer** (`* 1.01`) avoids re-entry on noise / wick touches exactly at
the prior ATH.

---

## Implementation changes

| Component | Change |
|---|---|
| `PrecomputeCycleTopMapAsync` | Return type changed to `(ZoneMap, AthMap)` tuple; `athMap[date] = cycleAth` populated in the same loop |
| `RunBacktestInternalAsync` main call site | Tuple-destructured: `var (cycleTopMap, cycleAthMap) = ...` |
| `RunBacktestInternalAsync` diagnostics call site | `var (cycleTopMapDiag, _) = ...` (AthMap discarded) |
| Simulation state | `double simMacroSellCycleAth = 0.0` added |
| macroOnly advice block | `isAthBreakout` computed; `"AthBreakoutBuy"` arm added (priority below `DistributionSell` and `StrongBuy`) |
| Sell state reset | `simMacroSellCycleAth` captured from `cycleAthMap` when `cycleTopForcedSell` |
| Buy success | `simMacroSellCycleAth = 0.0` cleared |

### Five buy gates bypassed for `AthBreakoutBuy` in macroOnly

All gates guarding against re-entry **after a cycle top** are bypassed with
`&& !(macroOnlyMode && advice == "AthBreakoutBuy")`:

1. **PostCycleTopBuyBlockDays** — day-count block would prevent re-entry for months
2. **PostCycleTopSmaRatioBlock** — BTC at new ATH is always above 2yr SMA
3. **PostCycleTopAthDrawdownBlock** — price IS the ATH, so the proximity ratio is ~100 %
4. **piCycleTopMap tight-loop guard** — old Pi Cycle Top no longer relevant
5. **cycleTopMap tight-loop guard** — old zone ATH is now support, not resistance
6. **ATH-Proximity buy-block gate** — same argument as #3

`"AthBreakoutBuy"` contains `"Buy"` so it enters the existing buy path automatically.

---

## Realtime parity

The `"AthBreakoutBuy"` signal is currently **backtest-only**.  Any realtime trading
decision policy that adopts macroOnly mode must be updated to:

- Track `simMacroSellCycleAth` (or equivalent) in persistent state across restarts
- Apply the same 1 % buffer and the same gate bypasses

This is tracked as a follow-up task.

---

## Alternatives considered

| Alternative | Reason rejected |
|---|---|
| Wait for `DeepCycleLow` only | Misses the entire 2020→2021 bull run (+260 %) |
| Re-enter immediately after DistSell | Too aggressive; DistSell zone can stay active for weeks |
| Use Pi Cycle Top cross-under as re-entry | Pi Cycle Top cross-over fires too early; cross-under logic adds complexity |
| 2 % buffer instead of 1 % | Backtest not significantly different; 1 % is already noise-resistant at BTC scale |

---

## Consequences

- **Expected performance**: ~+4 660 % compounded (2017–2026), beating HODL +1 731 %
- **Previous Phase 4 result**: +1 328 %
- No additional data fetches (cycleAthMap populated in the existing `PrecomputeCycleTopMapAsync` loop)
- 1 new test: `MacroOnly_AthBreakoutBuy_ReBuysAfterDistSell`
- 407 tests green
