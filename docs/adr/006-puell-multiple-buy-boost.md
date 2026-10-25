# ADR-006: Puell Multiple Approximate as Bear-Market Buy-Boost Gate

## Status
Implemented (disabled by default via `PuellMultipleBuyBoostEnabled: false`)

## Context
The existing bear-market filter (`BearMarketFilterService.IsBearMarketAsync`) blocks all buys when
the daily close is below the configured SMA. The ATH-drawdown override (`AthDrawdownOverrideEnabled`)
allows re-entry when price is sufficiently far below the all-time high — useful for deep-cycle troughs.

However, the ATH-drawdown threshold is backwards-looking (ATH keeps rising) and does not directly
capture on-chain valuation. The **Puell Multiple** is a well-known Bitcoin cycle indicator measuring
miner revenue stress: when it falls below ~0.5 the market is historically in a deep accumulation zone
with asymmetric upside.

On-chain data requires external API access. We can approximate it with:

```
puellApprox = dailyClose / SMA(365, dailyCloses)
```

This is fully computable from existing Binance OHLCV data with no additional API dependency.

## Decision
Add an optional, independent second override for the bear-market block:

> If `PuellMultipleBuyBoostEnabled = true` **and** `puellApprox < PuellMultipleBuyBoostThreshold`,
> the bear-market buy block is bypassed (same semantics as the ATH-drawdown override).

The two overrides are OR-combined: either condition is sufficient to allow a buy.

Default configuration ships **disabled** (`PuellMultipleBuyBoostEnabled: false`) with a conservative
threshold of `0.5`. The operator enables it via `appsettings.json` and can tune the threshold.

## Implementation

| Layer | Change |
|---|---|
| `BearMarketFilterOptions` | 3 new properties: `PuellMultipleBuyBoostEnabled`, `PuellMultipleBuyBoostThreshold`, `PuellMultipleInterval` |
| `IBearMarketFilterService` | New method `IsPuellMultipleUndervaluedAsync(interval, asOf, threshold, ct)` |
| `BearMarketFilterService` | Implements the method: fetches daily candles, computes SMA(365), evaluates ratio; 24 h cache |
| `TradeOpportunityService` | Puell override is checked after ATH-drawdown override fails (realtime path) |
| `BacktestService` | `PrecomputePuellMultipleMapAsync` pre-computes a `DateOnly→bool` map before the inner loop (O(n) lookup during replay) |
| `GetDecisionDiagnosticsAsync` | Updated to include the Puell gate in the diagnostics path |
| `appsettings.json` | 3 new keys added, `PuellMultipleBuyBoostEnabled: false` |

## Consequences
- **Positive**: Adds a valuation-based override that fires during historically high-conviction buy zones.
- **Positive**: Zero new external API calls; uses already-fetched Binance 1d candles.
- **Neutral**: SMA(365) requires ≥ 365 daily candles; before that the override never fires (fail-open: bear filter stays active).
- **Risk**: `puellApprox ≠ true Puell Multiple` (ignores miner revenue seasonality). May trigger on extended drawdowns that are not true cycle lows. Threshold should be validated against historical data before enabling.
- **Replay/realtime parity**: Both paths use the same threshold from `BearMarketFilterOptions`; decision policy is shared.

## Alternatives Considered
- **Real on-chain Puell Multiple API** (Glassnode/CryptoQuant): rejected — adds external API dependency and cost.
- **Lower ATH-drawdown threshold only**: not valuation-based; doesn't capture SMA-relative price level.
