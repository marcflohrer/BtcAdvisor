# 015: Pi Cycle Top Sell Gate

Date: 2026-05-09
Status: Accepted

## Context

The 2017–2026 backtest comparison showed that the existing price-proximity-based cycle-top
sell zone (`CycleTopSellZoneEnabled`) produced too many false positives (26 trades, 15.4%
win rate, -106.49% profit vs HODL +2069.6%). The problem: ATH proximity alone fires in
mid-cycle range-extension phases, not just at peaks.

The Pi Cycle Top indicator (111DMA vs 350DMA×2) is structurally different:
- It fires only when momentum is extremely elevated (short MA nearly equals 2× the long MA)
- Historically it fired within days of the 2013, 2017, and 2021 Bitcoin cycle tops
- It uses only daily OHLCV closes already fetched by `IBinanceKlineService` — no new API

## Decision

Add `IsPiCycleTopAsync(interval, asOfUtc, ct)` to `IBearMarketFilterService`, implemented
using `ILocalIndicatorComputationService.ComputeSma()` on the same daily candles already
cached in the system.

Signal definition: **111-day SMA of closes ≥ 350-day SMA of closes × 2**

When `PiCycleTopSellEnabled = true` and a position is open, any non-sell advice is upgraded
to `DistributionSell` — identical semantics to `CycleTopSellZoneEnabled`.

## Configuration

```json
"PiCycleTopSellEnabled": true,
"PiCycleTopInterval": "1d"
```

Default: enabled (`true`). The indicator only fires at genuine momentum extremes, so
false-positive risk is low.

The existing `CycleTopSellZoneEnabled` is set to `false` (production default). The Pi Cycle
Top gate supersedes it as the primary cycle-peak detector.

## Implementation

- `IBearMarketFilterService.IsPiCycleTopAsync` — new interface method
- `BearMarketFilterService.IsPiCycleTopAsync` — implementation with 24h IMemoryCache
- `TradeOpportunityService.ShouldForcePiCycleTopSellAsync` — realtime gate, called after `ShouldForceCycleTopSellAsync`
- `BacktestService.PrecomputePiCycleTopMapAsync` — O(n) pre-computation using full SMA series, keyed per `DateOnly`
- Applied at both backtest execution paths (main `RunBacktestCoreAsync` + `GetDecisionDiagnosticsAsync`)

## Replay/Realtime Parity

Both `TradeOpportunityService` (realtime) and `BacktestService` (replay) apply the same gate logic.
`ShouldForceCycleTopSellFromMap` is reused for the Pi Cycle Top map lookup (identical semantics: no sell already in progress, position open).

## Fail-safe

When fewer than 350 daily candles are available, the check returns `false` (fail-open: do not force sell). This prevents early-history false triggers.

## Consequences

Positive:
- Historically precise: triggered within days of each BTC cycle top
- No new external API dependency — reuses existing daily candle cache
- Low false-positive risk compared to ATH-proximity-only approach
- Configurable per environment

Trade-off:
- May miss cycles where momentum does not reach the classic threshold (diminishing returns each cycle)
- Does not help with mid-cycle corrections (correct behaviour: hold through them)

## Follow-up

Run 2017–2026 backtest with `PiCycleTopSellEnabled = true`, `CycleTopSellZoneEnabled = false`
and compare against HODL +2069.6% and the baseline FormulaByCycle +254.93%.
