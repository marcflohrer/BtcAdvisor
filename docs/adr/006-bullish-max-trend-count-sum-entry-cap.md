# ADR-006 — BullishMaxTrendCountSum Entry Cap

**Status:** Accepted  
**Date:** 2025  
**Context:** Entry quality filter based on TC correlation analysis

## Context

9-year backtest (2017→2026) with `d=7`, `cooldown=336h` baseline gives +280.6%, 10 closed trades, WR 30%.

Correlation analysis on entry metadata showed:

| AR | TC | n | Profit | WR | AvgMFE |
|---|---|---|---|---|---|
| 3 | 1 | 2 | +287% | 50% | +328% |
| 4 | 1 | 1 | +42% | 100% | +77% |
| 4 | 2 | 6 | −38% | 17% | +17% |
| 4 | 0 | 1 | −10% | 0% | 0% |

Key finding: **TC=2 entries cluster heavily in late-cycle bubble phases** (2018, 2019, 2021) where Bitcoin had already run significantly. High trend-count alignment at entry indicates a crowded/overextended move — not a dip-buying opportunity.

## Decision

Add `BullishMaxTrendCountSum` as an upper cap on entry TrendCountSum:

- **Default value:** `int.MaxValue` (no cap — backward compatible)
- **Recommended value:** `1` based on sweep results

The check `trendCountSum <= maxTrend` is enforced in `LongTimeframeConfluenceEvaluator.EvaluateCore()` after the existing min-trend check.

## Sweep Results (2017→2026)

| bullishMaxTrendCountSum | Profit | Trades | WR |
|---|---|---|---|
| ∞ (baseline) | +280.6% | 10 | 30% |
| 2 | +280.6% | 10 | 30% |
| **1** | **+329.1%** | **3** | **67%** |

## Trade Analysis (maxTC=1)

Surviving trades:
- 2020-03-08 HardStopLoss AR=3 TC=1 → −10.4%
- 2021-05-28 Sell AR=3 TC=1 → **+297.3%**
- 2024-09-20 Sell AR=4 TC=1 → **+42.2%**

Filtered out (all losses or low-gain): 7 trades including all 5 AR=4,TC=2 HardStops and 1 TC=0 HardStop.

## Risks and Limitations

- **n=3 trades** — extremely small sample. The WR improvement may not generalize.
- The 2023 Sell (+14.2%, AR=4, TC=2) is also blocked by maxTC=1, losing a small winner.
- Benchmark (HODL) is still +1940% vs +329% — significant gap remains.
- The parameter applies only to **bullish entries** — bearish exits are unaffected.

## API

The cap is exposed as `bullishMaxTrendCountSum` query parameter on:
- `GET /Backtest` (single run)
- `GET /Backtest/sweep`
- `GET /Backtest/walkforward`
- `GET /Backtest/diagnostics`

Default is `int.MaxValue` (no cap) to preserve backward compatibility.
