# ADR-009: ATH-Proximity Buy-Blocking Gate

**Status:** Accepted (revised)  
**Date:** 2025-01-01  
**Revised:** 2025-05-10 (ATH-age constraint added)  
**Deciders:** Marc

---

## Context

Backtesting (v4, 2020–2026) with Bug Fix 1 (Pi Cycle Top tight-loop) and Bug Fix 2 (cooldown default) applied
revealed strategy return of 421.8% vs HODL 937.4% (0.45× factor).

Four buy signals occurred while the BTC price was already near the all-time high (ATH):

| Date       | Buy Price   | ATH at time | Proximity | Outcome |
|------------|-------------|-------------|-----------|---------|
| 2021-05-13 | $58,241     | $64,899     | 89.7%     | −31.6%  |
| 2021-11-19 | $65,519     | ~$69,000    | 94.9%     | −35.3%  |
| 2024-06-24 | $66,000     | $73,750     | 89.5%     | −6.5%   |
| 2025-02-25 | $96,258     | ~$109,000   | 88.0%     | −11.5%  |

These buys collectively produced heavy losses because the strategy entered near local/cycle tops.

**Revised context (v7→v8):** The initial implementation (v7, 0.52×) blocked the 2024-10-15 buy at $62,870
(85.2% of ATH $73,750) because the ATH proximity gate was always active regardless of how long ago the ATH
was set. The ATH from March 2024 was already 215 days old in October 2024, and BTC was on its way to a new
cycle high of $109k — this was a pre-breakout accumulation entry, not a near-cycle-top entry.

Adding an ATH-age constraint (gate only active when ATH ≤ 180 days old) unblocked this buy (+8.2%),
improving strategy to 0.53× HODL.

---

## Decision

Add an **ATH-Proximity buy-blocking gate**: reject any new buy signal when:
1. The actual signal price exceeds `AthProximityBuyBlockMaxPercent` (default: 85%) of the causal running ATH, **AND**
2. The ATH was set within the last `AthProximityBuyBlockMaxAthAgeDays` (default: 180 days).

If the ATH is older than 180 days the gate is inactive — this indicates pre-breakout accumulation before
a new cycle high, where near-ATH proximity is expected and valid.

The ATH is calculated as the running maximum of 2h candle highs up to and including the candidate timestamp
(causal — no look-ahead). The comparison is against the **actual signal price** (`result.CurrentPrice`),
not the 2h candle close, because on volatile days the two can diverge significantly.

### Configuration (`BearMarketFilterOptions`)

```json
"AthProximityBuyBlockEnabled": true,
"AthProximityBuyBlockMaxPercent": 85.0,
"AthProximityBuyBlockInterval": "1d",
"AthProximityBuyBlockMaxAthAgeDays": 180
```

### Threshold Rationale

- 85% blocks all four bad trades (89.7%, 94.9%, 89.5%, 88.0%)
- Leaves most accumulation-zone entries unaffected (55–70% of ATH)
- 180-day age window allows pre-breakout entries when the old cycle ATH is stale
- 2024-10-15 at $62,870 (85.2% of ATH, ATH 215 days old): correctly allowed → +8.2%

---

## Implementation

### Backtest path (`BacktestService`)

- Helper `BuildRunningAthMap`: iterates 2h candles ordered by close time, tracks running ATH value
  **and the timestamp when the ATH was set** (`athSetAt`). Returns
  `IReadOnlyDictionary<DateTime, (double Ath, DateTime AthSetAt)>`.
- Pre-fetch 2h candles once before the main loop.
- Gate: if proximity > max% AND `(timestamp − athSetAt).TotalDays ≤ maxAthAgeDays` → `continue`.
- Signal price (`result.CurrentPrice`) is used for the proximity check (not the 2h candle close).

### Realtime path (`BearMarketFilterService.IsAthProximityTooHighForBuyAsync`)

- Signature extended: `(interval, asOfUtc, maxAthProximityPercent, maxAthAgeDays, ct)`.
- Uses `MaxBy(c => c.High)` to find the ATH candle, checks proximity AND age before returning `true`.
- Cached for 24 hours per unique `(interval, maxPercent, maxAgeDays, timestamp)` key.

### Replay/Realtime parity

Both paths use the same threshold and the same age constraint. Decision semantics are identical.

---

## Consequences

**Positive:**
- Avoids buying near fresh cycle tops; reduces maximum drawdown after entries.
- Age constraint prevents the gate from blocking legitimate pre-breakout accumulation entries.
- Simple, deterministic rule — no external oracle needed beyond existing candle data.

**Negative / Risks:**
- May still miss some re-entries if the bull market begins within 180 days of the last ATH.
- Old-ATH accumulation entries (gate inactive) can still lose money if the new cycle fails to materialise
  (e.g., Jan 2026 Buy $86,855 at 79.6% of $109k ATH, ATH 400 days old → allowed → −11.8%).
- Threshold calibrated on limited history; future cycles may warrant adjustment.

---

## Alternatives Considered

- **Dynamic threshold based on Puell Multiple or on-chain data**: more sophisticated but adds complexity.
- **Gate based on recent drawdown from ATH instead of proximity**: equivalent; proximity chosen for simplicity.
- **Per-cycle ATH reset**: rejected — cumulative ATH is more conservative.
- **Age constraint with shorter window (90 days)**: would have kept the Oct-2024 buy blocked; 180 days chosen
  to allow entries during the typical 2–6 month post-correction accumulation phase.
