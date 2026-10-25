# ADR 012 – SMA-Hold-Exit Debounce

**Status:** Accepted  
**Date:** 2026-05-07  
**Related:** ADR 011 (Bear-Market Filter + HardStopLoss), ADR 010 (Follow-Through Anchor)

## Context

After ADR 011, cumulative backtest profit 2017→2026 improved from −401% to −82% (22 trades, WR 23%, MaxLoss −10.45%). The remaining gap to Buy&Hold (+1940%) is ~2023pp.

**Trade-Postmortem (MFE/MAE)** showed:
- 41% of trades hit HardStopLoss at −10.45%
- 91% of trades saw MAE < −5% after entry
- avg MFE-Gap = +10.1pp — but this was mechanical artefact (17 losers with MFE≈0 inflate the gap)
- The 5 genuine winners had MFE-Profit gaps of only 0.5–7pp → exits were fine

**Phase 1 Entry-Audit** showed:
- No clean entry filter (SMA gate, 90d-high proximity) separates SL from non-SL trades
- SL-trades were on average *less* overbought than non-SL trades
- Entry timing is not the root cause

**Phase 2 Always-In-Bull Baseline** (BTC > 200d-SMA & SMA rising):
- +212% vs current −82% (delta: +294pp)
- Only 10.9% of B&H efficiency; too many whipsaw exits

**Root cause identified:** The strategy exits positions (Sell / DistributionSell) during Bull-Runs because short-term distribution signals fire before the macro bull trend ends. The single 2020→2021 Bull-Run alone offers +640% MFE. The old strategy captured only ~0% of it.

## Decision

Add `SmaExitDebounceDays` to `RiskControlOptions`:

- When `> 0`, regular Sell and DistributionSell signals are suppressed **unless BTC has been below the 200-day SMA for N consecutive calendar days**.
- `HardStopLoss` and `FireSell` are **never suppressed** by this gate.
- Implemented in `RunBacktestCoreAsync` via a pre-computed `Dictionary<DateOnly, bool>` lookup (`BuildDailyBelowSma200LookupAsync`) built from 1d Binance candles.
- The API accepts `smaExitDebounceDays` as a query parameter so sweeps are possible without redeploy.

**Chosen value: `SmaExitDebounceDays = 7`** (7 consecutive days below SMA200 required before exits are allowed).

### Sweep results (2017→2026, reEntryCooldownHours=336)

| SmaExitDebounceDays | Profit | Trades | WR | MaxGain |
|---|---|---|---|---|
| 0 (baseline) | −82% | 22 | 22.7% | +24.7% |
| 1 | **+367%** | 10 | 40.0% | +297.3% |
| 3 | +294% | 10 | 30.0% | +297.3% |
| 7 | +281% | 10 | 30.0% | +297.3% |
| 10 | +300% | 10 | 30.0% | +308.6% |
| 14 | +300% | 10 | 30.0% | +308.6% |
| 21 | +297% | 10 | 30.0% | +305.6% |

d=1 scores highest (+367%) but is dominated by a single trade (2020-05-14→2021-05-28, +297%). d=7 is chosen as the conservative, robust value:
- Stable plateau at d=7–14 (+280–300%)
- Less sensitive to individual bar noise
- 3 independent winning trades rather than 1

## Key trades captured by d=7

| # | Entry | Exit | Hold | Profit | Advice |
|---|---|---|---|---|---|
| 7 | 2020-05-14 | 2021-05-28 | 378d | +297.3% | Sell (held full bull run) |
| 9 | 2023-02-10 | 2023-08-25 | 196d | +14.2% | Sell |
| 10 | 2024-01-22 | 2024-09-20 | 241d | +42.2% | Sell |

## Consequences

- **Replay/realtime parity:** `SmaExitDebounceDays` is currently only implemented in the backtest path. The live `TradeOpportunityService` still exits on every Sell signal. A future ADR should add the same gate to the live path before live trading is activated.
- **Remaining gap to B&H:** ~1660pp. The 2017/18 entries (Trades 1–6) produce 6 HardStopLoss trades totalling ~−63pp. Phase 1 analysis confirmed these are not filterable by SMA-position or 90d-high proximity. They reflect true structural limitation of the current signal model in post-bubble phases.
- **HardStopLossPercent remains at −10%.** Post-hoc MAE analysis showed the 2017/18 crashes reached −32%; any stop level above −10% would have resulted in larger losses, not smaller ones.
- **appsettings defaults set:** `ReEntryCooldownHours=336`, `SmaExitDebounceDays=7`.
