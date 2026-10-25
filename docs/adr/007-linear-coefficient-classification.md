# ADR-007: Linear Coefficient Classification for Extrema Signals

## Status
Proposed

## Context

After implementing Oscillator-Only confluence (ADR-006), we discovered that the base gate `minAlignedRanges >= 3` is insufficient for bear-market bottoms. Historical analysis shows:

- **Problem**: Current confluence approach requires both oscillators AND moving averages aligned
- **Root Cause**: At true market reversals (e.g., 2022-06-18 local low at $19k), oscillators correctly signal bullish, but MAs remain structurally bearish (all prices below MAs)
- **Oscillator-Only Pivot**: Removed MA requirement, but this revealed a deeper issue: oscillator alignment alone has no quantitative threshold

### Test Case: 2022-06-18 Local Low ($19k bottom)

Neighborhood analysis (±7 days):

| Date       | OscBuy | OscSell | Strong | Medium | Bullish Score | Is Extremum |
|------------|--------|---------|--------|--------|---------------|-------------|
| 2022-06-13 | 17     | 2       | 2      | 0      | +17.4         | ❌ No       |
| 2022-06-18 | 13     | 1       | 1      | 1      | +13.7         | ✅ **YES**  |
| 2022-06-24 | 4      | 5       | 0      | 2      | +1.0          | ❌ No      |

**Key Insight**: 2022-06-18 is the TRUE LOCAL LOW, but it is NOT the best alignment day in the window (2022-06-13 scores higher). However, 2022-06-18 has stronger **trend confirmation** (1 strong + 1 medium trend indicator vs 2 strong, 0 medium on 2022-06-13).

## Decision

Implement a **linear classification model** that combines:

1. **Oscillator alignment** (primary signal)
2. **Trend confirmation** (differentiator for true reversals)
3. **Turning point detection** (separate ±30-day window mechanism)

### Linear Equation for Bullish Classification

```
f(osc_buy, osc_sell, strong_trend, medium_trend) =
  2.0 * osc_buy - 1.5 * osc_sell + 1.5 * strong_trend + 0.5 * medium_trend

Decision boundary: f(...) > 10 → BULLISH signal
```

### Coefficient Rationale

| Coefficient | Factor | Value | Rationale |
|-------------|--------|-------|-----------|
| a₁          | OscBuy | +2.0  | Primary signal for bullish reversal; directly indicates buy consensus |
| a₂          | OscSell| -1.5  | Counter-signal; penalizes remaining bears, but at lower weight (assumes some holdouts) |
| a₃          | StrongTrend | +1.5 | High confidence indicator; 1 strong trend ≈ 1 additional buy signal |
| a₄          | MediumTrend | +0.5 | Moderate confidence; provides tie-breaking boost for borderline cases |
| b           | Threshold | 10 | Minimum confidence level to trigger buy signal (tunable per backtest) |

### Architecture

```
┌─────────────────────────────────────────────────────┐
│ New Condition Class: LinearCoefficientBullishMet    │
├─────────────────────────────────────────────────────┤
│ 1. Accept oscillatorStatistics, movingAverageStats  │
│ 2. Extract features from best day in ±7 window:     │
│    • OscBuy, OscSell, StrongTrend, MediumTrend      │
│ 3. Evaluate: 2.0*buy - 1.5*sell + 1.5*st + 0.5*mt  │
│ 4. Return: result > 10                              │
│ 5. TradeAdvice: Buy (if true), or Sell (opposite)   │
└─────────────────────────────────────────────────────┘
```

## Consequences

### Positive
- **Quantitative decision boundary**: No more ambiguous "confluence feels strong" judgments
- **Trend validation**: Distinguishes true reversals (with trend confirmation) from noise
- **Backtest-driven coefficients**: Can A/B test different weights empirically
- **Explainable**: Each coefficient maps to a market signal (buy count, trend strength, etc.)

### Negative
- **Single-case fitting**: Coefficients derived from analysis of 2022-06-18; may not generalize
- **Threshold tuning required**: Boundary value (10) is arbitrary; must be validated across full historical dataset
- **Loss of anchor range logic**: Current model ignores `minAlignedAnchorRanges` (1d, 1w anchors); may need re-integration

### Mitigations
- Implement as additional gate layer: can be combined with existing confluence checks
- Empirical A/B test on 2020-2026 data to measure coverage improvement
- Use proposal endpoint to sweep threshold values and find optimal boundary

## Validation Steps

### Phase 1: Unit Tests
- [ ] Test with 2022-06-18 neighborhood data (should classify as BULLISH)
- [ ] Test with 2022-06-24 data (should classify as bearish at threshold 10)
- [ ] Test edge cases (zero trend indicators, all sell signals, etc.)

### Phase 2: Empirical Backtesting
- [ ] Run proposal endpoint with linear classifier on full 2020-2026 range
- [ ] Measure bullish/bearish coverage for different threshold values (8, 9, 10, 11, 12)
- [ ] Compare against current Oscillator-Only confluence (benchmark)
- [ ] Identify optimal threshold that maximizes coverage without too many false positives

### Phase 3: Integration
- [ ] Create `LinearCoefficientBullishConditionMet` class
- [ ] Create `LinearCoefficientBearishConditionMet` class (symmetric)
- [ ] Add to `PrimaryDecisionMaker.CreateTradeConditions()` list
- [ ] Deploy locally and test on running backtest
- [ ] If coverage improves (e.g., bullish 0% → 40%+), commit to main

## References

- **Prior ADR**: ADR-006 (Oscillator-Only Confluence) — foundation for this approach
- **Analysis Date**: 2026-05-01
- **Test Data**: 2020-01-01 to 2026-05-01 (full historical range)
- **Key Extrema**: 2022-06-18 local low ($19k), used as primary validation case
