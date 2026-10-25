# ADR 006 – Linear Threshold Calibration: 2023-2026 Window (Option C)

**Status:** Accepted  
**Date:** 2026-05-01

## Context

The LTF condition pipeline includes two linear scoring conditions:

- `LongTimeframeLinearBullishConditionMet` – fires when the bullish linear score exceeds a threshold.
- `LongTimeframeLinearBearishConditionMet` – fires when the bearish linear score exceeds a threshold.

Formula (per timeframe range, summed over 2h/4h/12h/1d/1w):
- **Bullish score:** `2.0×buy − 1.5×sell + 1.5×strongTrend + 0.5×mediumTrend`
- **Bearish score:** `2.0×sell − 1.5×buy + 1.5×strongTrend + 0.5×mediumTrend`

Previous default threshold was `10.0` for both directions, which was a placeholder and not calibrated against historical turning points.

## Analysis

A BIS group analysis was run against the API (2023-01-01 to 2026-04-30, BTC1D, 5 % price tolerance, 15-day match window) using `extrema_bis_group_search.py`.

**27 turning points total (2020-2026), 16 in the 2023-2026 window.**

The analysis found only 5 directional BIS candidates near missed turning points, forming 3 groups:

| Group | Count | Linear score min | avg |
|---|---|---|---|
| `Buy\|A4\|AN1\|T3_4` | 1 | 35.0 | 35.0 |
| `Buy\|A3\|AN1\|T3_4` | 1 | 19.5 | 19.5 |
| `Sell\|A3\|AN2\|T3_4` | 3 | 20.5 | 25.0 |

Error-rate sweep (2023-2026, 168 threshold combinations) showed:
- **Best achievable Bullish:** T1 = 50 %, T2 = 87.5 %
- **Best achievable Bearish:** T1 = 71.4 %, T2 = 87.5 %
- **No combination reaches the 5 %/5 % goal.**

Root cause: base rate of turning points (~0.56 % of days) combined with current alignment/confluence features does not provide sufficient discriminative power across the full 2020-2026 history.

## Decision

**Option C selected by user:** Restrict the calibration and evaluation window to **2023-01-01 – present**, where indicator data is richer (all 5 LTF ranges consistently populated).

Thresholds updated to the minimum observed linear score in BIS candidate groups:

| Condition | Old threshold | New threshold | Basis |
|---|---|---|---|
| `LongTimeframeLinearBullishConditionMet` | 10.0 | **18.0** | Min bullish BIS score (19.5), rounded down to 18 to allow slight noise |
| `LongTimeframeLinearBearishConditionMet` | 10.0 | **20.0** | Min bearish BIS score (20.5), rounded down to 20 |

Default start date for `error-rates` and `match-audit` endpoints updated from `2017-08-17` to `2023-01-01` to reflect the calibration window.

## Consequences

- The linear conditions are now more conservative: only fire when indicator alignment is strong enough to match historically observed turning-point patterns in the 2023-2026 period.
- False positives are reduced compared to threshold=10 (fewer weak confluence days trigger a signal).
- False negatives remain elevated: most 2020-2023 turning points are not recoverable with current features — this is accepted as an out-of-scope period for the new calibration.
- The 5 %/5 % error goal remains aspirational. A future ADR should revisit when either (a) more discriminating features are added, or (b) the ML project (`taapiio.machine.learning`) provides richer scores.
