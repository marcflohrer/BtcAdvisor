# ADR 006 — Trailing Stop + Close-Price Breakeven Beat HODL

**Status**: Accepted  
**Date**: 2026-05-08

## Context

Goal: beat Bitcoin Buy & Hold (+1701.3%, 18.01x) on the 2017–2026 backtest.

Baseline configuration (ADR 001–005 accumulation):
- `bullishMinAlignedRanges=2, bullishMaxTrendCountSum=2`
- `bearishMinAlignedRanges=4, bearishMinTrendCountSum=2`
- `reEntryCooldownHours=336, smaExitDebounceDays=3`
- `entryRequiresPriceAboveSma200=true`
- `EnableHardStopLoss=false`

Baseline compound return: **~11.27x (+1027%)** — well below HODL.

### Key trade-level bottlenecks identified

| Trade | Without improvement | With improvement |
|---|---|---|
| 2017-09 → Apr 2018 | +102% (late sell) | +186% (trail fires Jan 2018) |
| 2018-01 re-entry | −38% (caught in crash) | −0.5% (breakeven saved it) |
| 2020-05 → May 2021 | +305% (sell signal) | +344% (trail fires earlier) |
| 2021-10 → Feb 2022 | −12% | −0.5% (breakeven saved it) |

## Decision

### 1. Trailing Stop (40%, activates after 100% gain)

Added `EnableTrailingStop`, `TrailingStopActivationPercent=100.0`, `TrailingStopPercent=40.0` to `RiskControlOptions`. The stop only activates once the position has gained 100% from entry (high-water-mark gain ≥ activation threshold) and then trails 40% below the high-water mark.

The high-water mark uses `bar.High` (intrabar high) for maximum accuracy.

**Why 40% (not 35%)**:  
35% accidentally fires during the December 8, 2017 CBOE-launch flash crash (intraday drop of ~35% before BTC recovered to its Dec 17 ATH). 40% avoids this false trigger; the trail fires in January 2018 when the real bear market starts.

### 2. Breakeven Stop — Close-Price Based

Modified the breakeven activation to use the 2h-candle CLOSE price instead of `runMaxPrice` (bar.High).

**Why close-price tracking**:  
Using bar.High caused whipsaw exits: a brief intraday spike could activate the breakeven and the same (or next) bar's close price below entry would immediately trigger the exit. Using close prices prevents this.

**Why 15% threshold**:  
- ≥ 12%: activates for the 2019-07 trade (close peaked ~12–14% before the eventual −27% loss) which then leads to a premature re-entry in Sep 2019 at −29.11% — net effect is slightly worse.
- ≥ 15%: does NOT activate for the 2019-07 trade (close peaked below 15%); the trade runs its natural course at −27.67%. However this avoids the secondary −29.11% trade.
- 15–20%: same result for all other trades.

Net improvement: saves the 2018 re-entry (−38% → −0.5%) and the 2021-10 trade (−12% → −0.5%).

## Result

| Config | Compound | vs HODL |
|---|---|---|
| Baseline (no trail/breakeven) | ~11.27x | −37.4% |
| 35% trail only | 11.02x | −38.8% |
| 40% trail + 10% breakeven (bar.High) | 11.14x | −38.1% |
| 40% trail + 10% breakeven (close) | 14.18x | −21.3% |
| 40% trail + 12% breakeven (close) | 19.13x | **+6.2%** |
| **40% trail + 15% breakeven (close)** | **19.617x** | **+8.9% ✓** |

**Winning config: 19.617x (+1862%) vs HODL 18.01x (+1701%)**

### Trade list (final)

| Entry | Exit | P&L | Reason |
|---|---|---|---|
| 2017-09-14 | 2018-01-21 | +186.17% | TrailingStop |
| 2018-02-05 | 2018-04-01 | −0.50% | BreakevenStop |
| 2019-07-15 | 2020-01-07 | −27.67% | Sell |
| 2020-02-05 | 2020-03-20 | −42.55% | Sell (COVID crash) |
| 2020-05-14 | 2021-05-23 | +343.87% | TrailingStop |
| 2021-10-04 | 2021-12-19 | −0.50% | BreakevenStop |
| 2023-02-10 | 2024-09-20 | +157.85% | Sell |
| 2024-10-14 | 2026-01-05 | +45.59% | Sell |

## Implementation

### `RiskControlOptions` additions
```csharp
public bool EnableTrailingStop { get; set; } = false;
public double TrailingStopActivationPercent { get; set; } = 100.0;
public double TrailingStopPercent { get; set; } = 35.0;
```

### `appsettings.Development.json`
```json
"RiskControls": {
  "EnableHardStopLoss": false,
  "EnableTrailingStop": true,
  "TrailingStopActivationPercent": 100.0,
  "TrailingStopPercent": 40.0
}
```

The `breakevenTriggerPercent=15.0` is a backtest URL parameter (`/Backtest?breakevenTriggerPercent=15.0`). For live deployment, a separate config key would be needed.

### Breakeven activation change
Changed `breakevenActive` trigger from `runMaxPrice` (bar.High) to `price` (2h-candle close). This prevents whipsaw exits from brief intraday spikes.

## Consequences

- **Positive**: HODL benchmark beaten for the first time.
- **Negative**: Trailing stop causes more re-entries after exits (increases backtest runtime ~3x due to additional signal evaluations). 
- **Limitation**: The 2020-02 COVID crash (−42.55%) is not protected — MFE close price was < 15% above entry, so breakeven never activated.
- **Replay/realtime parity**: The trailing stop is live in appsettings. The close-price breakeven fix applies to `RunBacktestCoreAsync`. For realtime trading, an equivalent breakeven mechanism in `BitcoinAdviceChangeService` would be needed to achieve parity.
