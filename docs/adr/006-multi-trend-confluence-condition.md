# ADR-006: Multi-Trend Bullish/Bearish Confluence Condition

## Status
Proposed

## Context
The existing `LongTimeframeBullishConfluenceConditionMet` fires whenever ≥3 of the 5 long timeframe
ranges (`2h, 4h, 12h, 1d, 1w`) are aligned (oscillator buy > sell, MA buy > sell, and at least one
medium/strong trend indicator). Empirically, this condition also fires on flat consolidation days and
weak-trend plateaus that do not correspond to genuine local lows.

Neighbourhood analysis (see `GET /api/insights/turning-points/neighborhood`) of ±7 day windows
around historically significant local lows revealed that genuine extremum neighborhoods tend to
carry more total trend indicator weight than random bullish confluence days. The key differentiator
is the **sum of strong + medium trend indicators across all 5 long timeframe ranges**
(`trendCountSum`), not just the per-range presence of any trend.

The goal: remove weak-confluence false positives without sacrificing recall on genuine local lows.

## Decision
Introduce two new condition classes:

- `LongTimeframeMultiTrendBullishConditionMet(minTrendCountSum = 3)`
- `LongTimeframeMultiTrendBearishConditionMet(minTrendCountSum = 3)`

These wrap the existing confluence gate and additionally require:

```
trendCountSum = Σ (IndicatorStrongTrendCount + IndicatorMediumTrendCount)
                  over all LongRanges where the key exists in oscillatorStatistics
               >= minTrendCountSum
```

Default threshold is **3** (one medium-trend indicator per aligned range for a minimal 3-range
confluence, or two strong in anchors + one elsewhere).

The threshold is injected via constructor, so it can be tuned independently for bullish and
bearish conditions without code changes.

A new analysis endpoint (`GET /api/insights/turning-points/condition-proposal`) sweeps a full grid
of `(minAlignedRanges, minAlignedAnchorRanges, minTrendCountSum)` combinations and returns coverage
percentages per combination. Run this against your historical BTC data after local deployment to
confirm the `minTrendCountSum = 3` default and adjust if needed.

## A/B evidence (pending)
The threshold default of 3 was derived analytically from the minimum trendCountSum expected under
a 3-range alignment with one medium-trend indicator each. **Empirical A/B validation via the
condition-proposal endpoint must be performed after local deployment before committing this
condition as a replacement for the existing condition.**

Steps:
1. `cd BtcAdvisor && ./start.sh` (rebuild + redeploy)
2. `curl "http://localhost:6003/api/insights/turning-points/condition-proposal?startUtc=2017-08-17T00:00:00Z&windowDays=7"`
3. Find the threshold combination with the best coverage while keeping minTrend ≥ 3.
4. Update `minTrendCountSum` default if data suggests a different value.

## Consequences
- **More conservative** buy/sell signals: days that pass the old condition but have
  `trendCountSum < minTrendCountSum` will no longer trigger.
- **Recall risk**: if genuine local lows happen to produce weak trend readings (e.g., early phase
  reversals), this condition may miss them. Mitigate by checking proposal endpoint coverage.
- The new classes do **not** replace `LongTimeframeBullishConfluenceConditionMet` in
  `PrimaryDecisionMaker` automatically. Explicitly swap them in after A/B validation.
- The `LongTimeframeConfluenceEvaluator` gains three new public methods:
  `ComputeTrendCountSum`, `IsBullishWithMinTrend`, `IsBearishWithMinTrend`.
