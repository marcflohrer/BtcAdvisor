# ADR-008: Cycle-Top Confirmation-Window

## Status

**Accepted — production ON: `N = 1 day, Tolerance = 0 %` (armed-at-both only)** (2026-05-18, v3).

v3 reduces the confirmation window from `N = 2` to `N = 1` after a
compounded-vs-summed metric re-analysis on the full 2017-09 → 2026-04
backtest revealed that `N = 2` collapses to HODL-equivalent behavior at
parabolic cycle tops, while `N = 1` captures the 2021-10 top and improves
compounded equity from 27.5x (baseline) to 73.4x (≈ 4.7x HODL). See v3
section below for full details.

---

**v2 — Accepted (2026-05-15): `N = 2 days, Tolerance = 0 %` (armed-at-both only)**.

v2 decouples the two sub-checks (armed-at-both and ATH-growth-tolerance) and
tunes `N` against the live 2022-06-01 → 2026-05-14 backtest. The production
default is now **enabled** and **improves on the ADR-007 baseline**:

| Config                         | TotalSells | Profit       | MaxDD    | Notes                       |
|--------------------------------|------------|--------------|----------|-----------------------------|
| ADR-007 baseline (N=0)         | 1          | +445.99 %    | 37.08 %  | Sell 2025-05-23 @ \$111,327 |
| v1 N=14, tol=1.0 %             | 0          | null         | 49.84 %  | Killed the only top         |
| v2 N=14, tol=0 %               | 0          | null         | 49.84 %  | Armed-at-both alone too strict |
| v2 N=7, tol=0 %                | 0          | null         | 49.84 %  | Still too strict            |
| v2 N=5, tol=0 %                | 0          | null         | 49.84 %  | Cliff: no sell              |
| v2 N=3, tol=0 %                | 1          | +480.83 %    | 37.08 %  | Sell 2025-07-15 @ \$118,432 |
| **v2 N=2, tol=0 % (default)**  | **1**      | **+504.89 %** | **37.08 %** | **Sell 2025-08-14 @ \$123,338** |

The v2 defaults filter the 2025-05-23 single-spike (premature exit) and shift
the sell ~3 months later, closer to the actual cycle top, lifting profit by
**+59 pp** over baseline at unchanged drawdown.

## Context

ADR-007 introduced the Halving-Window-Gate, restricting `DistributionSell` to
the post-halving window `[12, 30]` months. Live backtest 2022-06-01 → 2026-05-14
lifted profit from **+235.98 %** (gateless raw zone) to **+445.99 %** with a
single sell at 2025-05-23 @ \$111,327.

That win is statistically thin: it relies on **one** cycle-top data point. The
raw zone (drawup ≥ 350 %, ATH-proximity ≥ 90 %) can also fire on
**single-day spikes** that are not real tops — e.g. a brief \$130 k high that
later gives way to a \$160 k actual top. Without a second, orthogonal gate the
system would exit on the first wick that brushes the zone.

The two protections (halving window and confirmation window) are orthogonal:
- Halving window filters by **time-since-halving** (macro / structural).
- Confirmation window filters by **persistence of the raw signal** (price
  pattern / micro).

A single false top inside the halving window is exactly the scenario that
the confirmation gate must catch.

## Decision

Add a **Confirmation-Window** gate with **two independently switchable sub-checks**:

1. **Armed-at-both** (always active when `N > 0`): the raw CycleTopZone must
   be armed at both the evaluation date `d` and the prior date `d − N`
   (production default **`N = 2` days**).
2. **ATH-growth tolerance** (active only when `tolerance > 0`): the cycle
   ATH at `d` must not exceed `athAtArmed × (1 + tolerance)` (production
   default **`tolerance = 0 %` = disabled**).

The gate is ANDed with the raw zone and the halving gate. All three must be
true for a `DistributionSell` (and for the buy-side suppression that mirrors
it during a confirmed top).

**Decoupling rationale (v2, 2026-05-15):** the v1 design required both
sub-checks together, which killed the live 2025-05-23 sell during a
still-rising-ATH phase. Live tuning showed the armed-at-both sub-check alone
(`tolerance = 0`) already filters single-day spikes effectively, and an N
sweep produced a clear optimum at `N = 2` that beats the baseline.

## Implementation

### Config (`BearMarketFilterOptions`)
- `CycleTopSellZoneConfirmationDays` (int, code default **0** = disabled;
  appsettings.json sets **2**).
- `CycleTopSellZoneConfirmationAthTolerancePercent` (double, code default
  **1.0 %**; appsettings.json sets **0** = ATH-tolerance check disabled).

The code-level default of `0` for days keeps the existing tests passing
unchanged. Production opts in via `appsettings.json`.

### Backtest path (`BacktestService.PrecomputeCycleTopMapAsync`)
1. Extend `uniqueDates` backward by `confirmationDays` days **and** fill all
   gaps between `minD` and `maxD`, so that `map[d − N]` is always present.
2. Compute the raw zone map first (unchanged inner loop).
3. **Post-pass:** for every armed date `d`, clear it if `map[d − N]` is
   false/missing. Additionally, **only when `tolerance > 0`**, also clear it
   if `athMap[d] > athMap[d − N] × (1 + tolerance)`. With the production
   default `tolerance = 0`, the ATH-growth check is skipped entirely.

### Realtime path (`TradeOpportunityService`)
- Private helper `IsCycleTopConfirmationSatisfiedAsync(utcTradingDate, ct)`:
  1. Returns `true` immediately when `CycleTopSellZoneConfirmationDays <= 0`.
  2. Calls `IsCycleTopOvervaluedAsync` at `armedAt = utcTradingDate − N`;
     returns `false` if not armed.
  3. Returns `true` immediately when `tolerance <= 0` (ATH-growth check
     disabled — production default path).
  4. Otherwise calls `GetCycleAthAsync` at `utcTradingDate` and at `armedAt`
     and applies the tolerance comparison.
- ANDed into both call sites:
  - the buy-suppression block (~entry), and
  - `ShouldForceCycleTopSellAsync` (~exit).
- **Deliberately no new interface method** — keeps all existing Moq setups
  working unchanged.

## Consequences

### Positive
- Robust to single-day wicks and short-lived spikes inside the halving window.
- Cheap (one extra map read in backtest; two extra service calls in realtime).
- Orthogonal to the halving gate; the two compose.
- Backward-compatible defaults (`0` = off) keep all existing tests green.

### Negative
- A genuine sharp top followed by an immediate crash is exited **up to N days
  late**. The ATH-tolerance check rejects confirmation when price still
  extends, so in practice only flat/declining tops are confirmed late — but
  that's still a measurable give-back near the peak.
- Increases dependency on two services in realtime (latency doubles at the
  confirmation check, run at most once per evaluation cycle).

### Trade-offs Considered (live N-sweep, 2026-05-15)
- **N = 14, 7, 5 (tol=0):** all three suppress *every* candidate sell against
  the live data — too strict, baseline broken.
- **N = 3 (tol=0):** +480.83 %, sell 2025-07-15.
- **N = 2 (tol=0):** **+504.89 %, sell 2025-08-14** — chosen production default.
- **N = 14 with tol = 1.0 % (v1):** broken, killed baseline (see Status table).
- **N = 0 (off):** falls back to ADR-007 baseline +445.99 %.

### Replay/Realtime Parity
Backtest post-process map and realtime `IsCycleTopConfirmationSatisfiedAsync`
both implement the same decoupled semantics:
- Both compare raw zone at `d` vs. `d − N` (armed-at-both, always when `N > 0`).
- Both skip the ATH-tolerance comparison when `tolerance <= 0`.
- Both apply the same ATH-tolerance ratio when `tolerance > 0`.

## Verification

- Unit tests:
  - `RunBacktestAsync_CycleTopConfirmationDays_BlocksSingleDaySpike`
    — single-day spike at `T0+1day` (no arming at `T0-13d`) is suppressed.
  - `RunBacktestAsync_CycleTopConfirmationDays_AllowsSell_WhenPreviouslyArmedWithStableAth`
    — sell fires when zone is armed at both `d` and `d-N` with unchanged ATH.
  - `RunBacktestAsync_CycleTopConfirmationDays_AllowsSell_OnRisingAth_WhenToleranceDisabled`
    (v2) — sell fires when armed-at-both passes and the ATH-growth check is
    skipped via `tolerance = 0`, even though ATH grew +100 %.
- Full suite: **428 passed, 7 skipped, 0 failed**.
- Live backtest 2022-06-01 → 2026-05-14 with production defaults
  `N = 2, tol = 0 %`:
  - TotalBuys = 1 (2022-06-18 @ \$20,390, StrongBuy).
  - TotalSells = 1 (2025-08-14 @ \$123,338, DistributionSell).
  - Profit = **+504.89 %** (vs ADR-007 baseline +445.99 %, +59 pp).
  - maxDrawdown = 37.08 % (unchanged vs baseline).
  - buy-and-hold = 148.74 %.

## v3 (2026-05-18) — Reduce `N` from 2 → 1

### Trigger
User critique on the v2 production deployment: *"Die Strategie ist nun
identisch zu HODL"*. Re-analysis on the full 2017-09 → 2026-04 backtest
(macroOnly, persist=true) revealed two compounding issues:

1. **Metric misread.** The `totalProfitPercent` field is the **sum** of
   per-deal returns, not the compounded equity curve. With v2 (N=2, 99 %
   proximity) the single 1-deal trace summed to +2645 % vs HODL +1539 % —
   appearing to win — but compounded equity was only **27.5x** (HODL 16.4x;
   ratio 1.7x), barely above buy-and-hold once drawdowns were accounted for.
   The user's HODL-equivalence intuition was correct.

2. **Raw-zone collapse at parabolic tops.** `PrecomputeCycleTopMapAsync` uses
   `latestHigh = High[toIdx-1]` (yesterday's high, deliberate
   anti-look-ahead — see `BacktestService.cs` lines 2174-2179). At parabolic
   peaks the ATH is set on the same candle, so on the day after ATH,
   `athProx = High[ATH-day] / cycleAth ≈ 100 %`. The raw-zone window at 99 %
   proximity therefore lasts **~2 days** around each parabolic top. With
   `N = 2`, confirmation requires `raw[d] ∧ raw[d-2]` — a 3-day span — which
   **no date in the 2021-11 cycle satisfies**. The 2021 top is silently
   skipped; the next sell only fires in 2025-08, after a 77 % drawdown.

### Compounded comparison (full 2017-09 → 2026-04, macroOnly)

| Variant                    | Compounded | Summed   | MaxDD  | TimeInMarket | Deals |
|----------------------------|-----------:|---------:|-------:|-------------:|------:|
| Baseline v2 (99 %, N=2)    | **+2645 %** | +2645 %  | 77.2 % | 77.7 %       | 1     |
| B (95 %, N=2)              | +6958 %    | +1693 %  | 70.4 % | 67.0 %       | 2     |
| **v3 (99 %, N=1)**         | **+7240 %** | +1720 %  | **70.4 %** | 67.6 %   | **2** |
| HODL                       | +1539 %    | —        | —      | 100 %        | —     |

v3 → **73.4x equity** vs baseline **27.5x** (2.7x improvement) and HODL
16.4x (4.7x improvement), with **lower** maxDD (70.4 % vs 77.2 %).

### v3 deals (persist=true, 2026-05-18)
| Entry                              | Exit                                       | Per-deal return |
|------------------------------------|--------------------------------------------|----------------:|
| 2018-11-24 StrongBuy @ \$4,493.35  | 2021-10-22 DistributionSell @ \$62,566.89  | +1292.43 %      |
| 2022-06-18 StrongBuy @ \$20,390.01 | 2025-05-24 DistributionSell @ \$107,479.24 | +427.12 %       |

The 2021-10-22 sell (~16 days before the actual cycle top at \$69 k) is the
key recovery: with `N = 1` only `raw[d] ∧ raw[d-1]` (2-day span) is required,
which is satisfied during the late-October 2021 parabolic run.

### Decision (v3)
- `CycleTopSellZoneConfirmationDays = 1` (was `2`).
- `CycleTopSellZoneMinAthProximityPercent = 99` (unchanged from v2).
- `CycleTopSellZoneConfirmationAthTolerancePercent = 0` (unchanged).
- All other gates (halving window [12, 30] months, MinDrawup 350 %, Pi-Cycle)
  unchanged.

### Lessons (v3)
- **Always evaluate compounded equity** when comparing multi-deal strategies.
  Summed returns silently weight a 1-deal trace as if it were a multi-deal
  trace and can flip the ranking.
- The anti-look-ahead `latestHigh = High[toIdx-1]` is correct but makes the
  raw-zone window short at parabolic peaks. Confirmation N must be small
  enough to fit inside that window or it eliminates all parabolic tops.
- v2's apparent "improvement" of +59 pp summed (on the 2022-2026 sub-window)
  hid the fact that the same setting **misses the 2021 cycle top entirely**
  on the full multi-cycle backtest.

### Production state (v3)
| Field                                                  | Value |
|--------------------------------------------------------|------:|
| `CycleTopSellZoneEnabled`                              | false (CycleTopSellZone path) |
| `CycleTopSellZoneMinAthProximityPercent`               | 99    |
| `CycleTopSellZoneConfirmationDays`                     | **1** |
| `CycleTopSellZoneConfirmationAthTolerancePercent`      | 0     |
| `CycleTopSellZoneMinMonthsAfterHalving`                | 12    |
| `CycleTopSellZoneMaxMonthsAfterHalving`                | 30    |
| `PiCycleTopSellEnabled`                                | true  |

DB backup before v3 deploy: `backups/backup_2026-05-18_06-35-39_pre-N1.sql.gz`.

## Follow-up

- Monitor realtime: confirm `N = 1` does not introduce single-day-wick
  false exits during the next 6-12 months.
- Re-evaluate `N` after the next halving cycle: the parabolic-top
  raw-zone-collapse argument is structural and should persist, so `N = 1`
  is expected to remain optimal.
- Consider regime-aware tuning only if compounded equity stops improving
  vs HODL over a future cycle.
