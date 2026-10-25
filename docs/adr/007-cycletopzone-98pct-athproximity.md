# ADR-007: CycleTopZone — Raise ATH-Proximity Sell Threshold from 90 % to 98 %

## Status
Accepted

## Context

`PrecomputeCycleTopMapAsync` marks `zone[date] = true` when two conditions are met:

1. **Drawup** from the cycle low ≥ 350 %
2. **ATH proximity** (`latestHigh / cycleAth`) ≥ `CycleTopSellZoneMinAthProximityPercent`

With the Phase 3 closed-candle fix (`latestHigh = ordered[toIdx-1].High`), `latestHigh` is the
prior day's fully-closed daily candle. The sell executes at the first 2h bar (~02:00 UTC).

At the old threshold of **90 %**, the zone fired when the prior day's High reached 90 % of the
rolling 4-year ATH. For the 2022-bear-cycle buy at ~$20,390:

- Rolling cycleAth (Nov 2021) = $68,789
- Sell trigger at 90 %: $61,910 → execution price $61,376 (Feb 2024)
- Actual cycle ATH: $73,737 (14 Mar 2024), reached 3 weeks later

The 10 % discount below the prior ATH prematurely terminated the position. The real distribution
phase was still 3 weeks away.

## Decision

Raise `CycleTopSellZoneMinAthProximityPercent` from **90.0 to 98.0** in `appsettings.json`.

At 98 %, the trigger is `latestHigh / cycleAth ≥ 0.98`, meaning:

- In a rising market (prior day < cycleAth): fires when within **2 % of the rolling 4-year ATH**
- At the ATH (prior day = cycleAth): fires immediately (100 % ≥ 98 %)
- On retracement days from ATH: fires when yesterday's High ≥ 98 % of the peak

This better aligns with the macro thesis: only exit when the market is clearly in the upper 2 %
of its prior cycle range, not when it is merely approaching the old top.

## Impact on buy-side

The `CycleTopZone` buy guard (`if cycleTopIsActive → continue`) blocks new entries when the zone
is active. At 98 %, the zone is inactive for the 90–98 % range, slightly widening the re-entry
window in the next cycle. Deep-cycle-low entries (< 30 % of ATH) are unaffected.

## Realtime parity

`BearMarketFilterService.IsCycleTopOvervaluedAsync` reads the same config value. Both the
realtime signal and the backtest use 98 % after this change. No divergence.

## Backtest results (2017–2026, macroOnly)

| Metric | Phase 3 (90 %) | Phase 4 (98 %) | Delta |
|---|---|---|---|
| 2020-cycle exit | $17,739 | **$19,100** | +$1,361 |
| 2022-cycle exit | $61,376 | **$68,506** | +$7,130 |
| Compounded profit | +1,088 % | **+1,328 %** | +240 pp |
| Time in market | 36.9 % | 37.2 % | — |
| Max drawdown | 70.4 % | 70.4 % | — |
| Buy-and-hold | +1,731 % | +1,731 % | — |

## Consequences

- Exits are delayed by ~2 weeks per cycle, capturing more of the distribution phase.
- Slightly higher risk of holding into a sharp correction near the ATH. Acceptable given the
  strategy's macro horizon and the fact that macroOnly stop losses are disabled.
- No code changes; config-only. Tests unaffected (unit tests override options explicitly).
