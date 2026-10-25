# ADR 008 – Same-Direction Signal Follow-Through Filter

**Status:** Accepted
**Date:** 2026-05-03

## Context

The LTF confluence thresholds reduce noisy raw signals, but they still allow repeated same-direction signals that move the market price in the wrong direction.

Examples:

- A second buy signal at a lower price than the previous accepted buy does not confirm bullish continuation.
- A second sell signal at a higher price than the previous accepted sell does not confirm bearish continuation.

This creates two problems:

- visually noisy signal clusters on the chart,
- weak raw signals leaking into replay and realtime despite passing the indicator confluence gate.

Per product rules, replay and realtime must keep one shared decision policy.

## Decision

Introduce a shared follow-through filter for accepted same-direction signals:

- bullish signals are only accepted if price is at least `BullishMinFollowThroughPercent` above the previous accepted bullish signal price,
- bearish signals are only accepted if price is at least `BearishMinFollowThroughPercent` below the previous accepted bearish signal price.

The filter is applied in both:

- `TradeOpportunityService` for realtime persistence,
- `BacktestService` for historical replay/backtests.

Thresholds are calibrated with a dedicated insights sweep over historical BTC data, evaluated against existing turning-point Type-I/Type-II metrics and signal frequency.

## Consequences

- Consecutive same-direction raw signals now require directional price continuation, not just another condition hit.
- Backtests and persisted logs become less noisy and more interpretable.
- Buy and sell thresholds can be tuned independently instead of forcing a symmetric rule.
- Threshold changes remain measurable through the new follow-through sweep endpoint before rollout.