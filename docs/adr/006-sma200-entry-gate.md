# ADR 006 — SMA200 Entry Gate

**Status**: Implemented  
**Date**: 2025-01

## Context

The backtest (2017–2026, maxTC=1, cooldown=336h, smaExitDebounceDays=7) produces 10 trades with +339.2% vs +1701.3% HODL. Six of those trades are HardStopLoss exits with MAE between -14% and -33%. The hypothesis is that buying when BTC is already below its 200-day SMA increases loss risk, since the SMA200 is a widely-watched trend indicator and being below it is a structural bear signal.

## Decision

Add a new optional backtest parameter `entryRequiresPriceAboveSma200` (default `false`). When `true`, any buy signal is blocked on days where the daily close is below the SMA200. This reuses the existing `BuildDailyBelowSma200LookupAsync` / `dailyBelowSma200` infrastructure already built for the SMA exit debounce feature.

## Implementation

- `RunBacktestCoreAsync`: expand `dailyBelowSma200` build to trigger when `entryRequiresPriceAboveSma200 = true` (in addition to `effectiveSmaExitDebounceDays > 0`). After cooldown check, before trade entry: skip if `dailyBelowSma200[entryDate] == true`.
- Full chain updated: `RunBacktestAsync` → `SweepThresholdsAsync` → `WalkForwardAsync` → models (`BacktestSweepRow.EntryRequiresPriceAboveSma200`, `BacktestWalkForwardFoldRow.BestEntryRequiresPriceAboveSma200`) → interface → controller (`[FromQuery]` params on all three endpoints).
- Shorter overloads pass `[false]` to maintain backward compatibility.

## Volume Indicator Activation

Simultaneously enabled `VolumeVolatility.EnableScoreBonus = true` in `appsettings.Development.json`. OBV, VWAP, MFI, and Bollinger Band squeeze/expansion now contribute to the regime score bonus in all backtests. Candle data is sourced from the existing 1d kline DB cache — no additional API calls required.

## Outcome

| Config | Profit% | Trades | WR% |
|---|---|---|---|
| gate=false (baseline) | +339.2% | 10 | 40% |
| gate=true | **+349.6%** | 9 | 44% |
| HODL | +1701.3% | — | — |

The gate blocked 1 of 6 stops: the 2019-11-24 entry (~$7k, BTC below SMA200 during post-2018-bear recovery). The remaining 5 stops occurred when BTC was above SMA200 at entry, so the gate cannot filter them.

## Tradeoffs

- Marginal improvement (+10.4% saved on one trade).
- Does not block the five 2017–2020 stops that occur above SMA200.
- No false negatives: the 2021 +297% trade and all 2023–2026 winners are unaffected.
- Gate is off by default — no impact on realtime trading unless explicitly enabled.

## Alternatives Considered

- **SMA50/100 gate**: faster-moving, might catch more of the early-trend entries, but also risks blocking valid early-bull entries.
- **Bear market filter (already active)**: catches structural bear markets but not "above SMA200 but near a local top" entries like 2017-09.
- **ATR-based stop widening**: might survive the MAE drawdowns but at cost of larger per-trade losses.
