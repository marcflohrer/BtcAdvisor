# ADR-006: CycleTopZone — Use Previous Closed Candle to Eliminate Look-ahead Bias

## Status
Accepted

## Context

`PrecomputeCycleTopMapAsync` in `BacktestService.cs` computes `zone[date] = true` for each
backtest date using `asOfDt = date 23:59:59 UTC` as the window upper bound. This includes the
daily candle whose `CloseTime` falls on that date (Binance convention: `CloseTime ≈ 23:59:59`).
The loop therefore set `latestHigh = ordered[toIdx].High` — the **full-day High** of today's
candle, which is only finalized at end-of-day.

The backtest sell executes at the **first 2h candle close of that same date** (~02:00 UTC). This
creates ~22 h of look-ahead: the zone fires based on a price that is only reachable later in
the day, while the order fills far earlier at a lower price.

**Concrete example (Feb 28, 2024)**:
- Daily High Feb 28 = $63,500 → `zone[Feb 28] = true`
- First 2h close Feb 28 = $56,772 → sell executes at **$56,772**
- If Feb 27's High was < $61,910: with the fix, `zone[Feb 28]` uses Feb 27's High → false;
  Feb 28's High triggers `zone[Mar 1]` → sell executes on Mar 1's first 2h bar ≈ $62–64k

## Decision

In `PrecomputeCycleTopMapAsync`, change the loop so `latestHigh` is set only for iterations
`i < toIdx` (i.e., all candles except the last). After the loop `latestHigh = ordered[toIdx-1].High`
— yesterday's fully-closed daily candle, which is observable before the sell executes.

`cycleAth` and `cycleLow` continue to use the full window (including `ordered[toIdx]`) so the
cycle extremes remain accurate.

The guard `(toIdx - fromIdx + 1) < 2` (minimum 2 candles) ensures `toIdx - 1 >= fromIdx` is
always valid when the logic is reached.

## Realtime parity

`BearMarketFilterService.IsCycleTopOvervaluedAsync` calls `GetCandlesUpToAsync(interval, asOfNow)`
during the trading day. The current daily candle's `CloseTime` lies in the future, so it is
excluded by the `Where(c => c.CloseTime <= asOfNormalized)` filter. `ordered[^1]` is therefore
already yesterday's fully-closed candle. **No change required** to the realtime path.

## Consequences

- Sell timing shifts by ~0 to 1 day depending on whether the prior day's High already met the
  threshold. In practice this produces a higher execution price per cycle exit.
- Backtest results are now consistent with the realtime signal: both use the last *closed* daily
  candle as the zone trigger.
- Two unit tests updated to reflect the closed-candle convention:
  - `Setup1dCandlesCycleTopZone`: expensive candle moved from `i=99` to `i=98`
    (yesterday's closed candle triggers the zone for the sell date).
  - `BuySignalWhenCycleTopZoneActive_BlocksBuy`: candle 0 High raised to 46,000
    (must be expensive so `ordered[toIdx-1]` triggers the zone at the buy date).
