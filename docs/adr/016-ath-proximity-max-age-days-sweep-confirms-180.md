# ADR-016: AthProximity MaxAthAgeDays Sweep Confirms 180-Day Default

**Status:** Accepted
**Date:** 2025-07-11
**Deciders:** Marc

---

## Context

During analysis of the Feb–Oct 2024 backtest gap (strategy produced 0 buys while BTC rallied from
$48k to $63k), the `AthProximityBuyBlock` gate (introduced in ADR-009) was confirmed as the
blocking mechanism.

**Root cause of the gap:**
- BTC set a new ATH of ~$73,750 on 2024-03-14
- Gate threshold: price > 85% of ATH ($62,688) AND ATH age ≤ 180 days
- Block window: 2024-03-14 → 2024-09-11 (180 days)
- First valid buy: 2024-10-15 at $62,870 (ATH age = 215 days — gate no longer active)

To investigate whether the default of 180 days was optimal, a query parameter
`athProximityBuyBlockMaxAthAgeDays` was added to the backtest API to enable sweeping.

---

## Sweep Results

### 2024-2026 (period of the investigated gap)

| MaxAthAgeDays | Profit   | Buys | Sells | First gap entry  | Notes                          |
|--------------|----------|------|-------|------------------|--------------------------------|
| -1 (off)     | +51.11%  | 3    | 2     | 2024-04-30 $63k  | Open position at end of window |
| 30           | +51.11%  | 3    | 2     | 2024-04-30 $63k  | Same as -1 (47d > 30d)         |
| 60           | +44.69%  | 2    | 2     | 2024-05-25 $66k  | Worse exit timing              |
| 90           | +50.92%  | 2    | 2     | 2024-06-24 $63k  | Slightly worse                 |
| 120          | +40.97%  | 2    | 2     | 2024-08-03 $68k  | Worse — entry near local top   |
| **180 (cur)**| **~+51.65%** | **2** | **2** | **2024-10-15 $63k** | **Best closed-trade return**  |

HODL 2024-01-01 → 2026-01-01: +108%.

The counter-intuitive finding: **earlier entries in April–August 2024 produced worse results**
because the corresponding sell signal fired in April 2025 at $85,179 — missing the Jan 2025
local peak at $98,364. The October 2024 entry (enabled by the 180-day block expiring) caught
the superior Nov 2024–Jan 2025 bull run:
- days ≤ 30: Buy Apr 2024 @$63k → Sell Apr 2025 @$85k = +34.9%
- days = 180: Buy Oct 2024 @$63k → Sell Jan 2025 @$98k = +56.4%

### 2017-2024 parity check

All tested values (-1 through 180) produce **identical results**:
- Profit: +920.27%, Buys: 3, Sells: 2

The gate does not fire in 2017-2024 because during the 2021-2022 cycle top, BTC crashed
from $69k to below $20k so quickly that virtually no buy signals appeared within the
proximity window above 85% of ATH.

---

## Decision

**Keep `AthProximityBuyBlockMaxAthAgeDays = 180` (no change to appsettings.json).**

Rationale:
1. 180 days achieves the highest closed-trade return (+56%) for the critical 2024 cycle.
2. Reducing the window shifts entries earlier but creates worse exit alignment.
3. 2017-2024 behavior is identical across all tested values.
4. The Feb–Oct 2024 buy gap was caused by the gate, but that gap was **beneficial**: it
   prevented suboptimal entries and waited for a better risk/reward setup.

The `athProximityBuyBlockMaxAthAgeDays` query parameter remains available for future research.
Passing `-1` disables the gate entirely (useful for diagnostics).

---

## Implementation

- `BacktestService.RunBacktestCoreAsync`: `int athProximityBuyBlockMaxAthAgeDaysOverride = 0`
  - `0` = use appsettings value
  - `> 0` = override days
  - `-1` = disable gate entirely
- `BacktestController`: `[FromQuery] int athProximityBuyBlockMaxAthAgeDays = 0`
- `IBacktestController`, `IBacktestService`: updated interface signatures

## Replay/Realtime Parity

Not applicable — this is a backtest research parameter only. The realtime gate uses the
appsettings value directly (unchanged at 180 days).
