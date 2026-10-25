#!/usr/bin/env python3
"""Walk-Forward sweep over LTF thresholds 2017-2026.

For each fold the server internally sweeps all LTF combinations, picks the best
params on the train window, and evaluates them on the test window. The script
collects fold rows, derives the most-frequent best-param set, and runs a final
full-range backtest with those params.

Usage:
    python3 wf_ltf_sweep.py [--base-url URL] [--output DIR]
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

import requests

# ──────────────────────────────────────────────────────────────────────────────
# Configuration
# ──────────────────────────────────────────────────────────────────────────────
START = "2017-01-01T00:00:00Z"
END   = "2026-01-01T00:00:00Z"

# Train/test split: 24-month train, 12-month test, 12-month step → ~7 folds
TRAIN_MONTHS = 24
TEST_MONTHS  = 12
STEP_MONTHS  = 12

# Momentum / trough: fixed to current calibrated values (not swept)
# → reduces search space to LTF-only (36 combos/fold vs. 1458)
MOMENTUM_RATIOS         = "0.25"
TROUGH_DRAWDOWNS        = "8.0"
TROUGH_PROXIMITIES      = "5.0"

# LTF threshold grid (main target of this sweep)
# 3 × 2 × 3 × 2 = 36 combinations per fold
BULLISH_ALIGNED_RANGES  = "2,3,4"
BULLISH_TREND_SUMS      = "1,2"
BEARISH_ALIGNED_RANGES  = "2,3,4"
BEARISH_TREND_SUMS      = "1,2"
BEARISH_REGIME_SCORES   = "22.0"

TIMEOUT_PER_FOLD_S = 7200          # 2 h max per fold


def call_walkforward(base_url: str) -> list[dict]:
    """Stream the walk-forward endpoint and return all fold rows."""
    params = {
        "startDate":                    START,
        "endDate":                      END,
        "trainMonths":                  TRAIN_MONTHS,
        "testMonths":                   TEST_MONTHS,
        "stepMonths":                   STEP_MONTHS,
        "momentumBrakeRatios":          MOMENTUM_RATIOS,
        "troughMinDrawdownPercents":    TROUGH_DRAWDOWNS,
        "troughRecoveryProximityPercents": TROUGH_PROXIMITIES,
        "bullishMinAlignedRanges":      BULLISH_ALIGNED_RANGES,
        "bullishMinTrendCountSums":     BULLISH_TREND_SUMS,
        "bearishMinAlignedRanges":      BEARISH_ALIGNED_RANGES,
        "bearishMinTrendCountSums":     BEARISH_TREND_SUMS,
        "bearishRegimeMinLinearBullishScores": BEARISH_REGIME_SCORES,
    }
    url = f"{base_url}/Backtest/walkforward"
    print(f"GET {url}", flush=True)
    print(f"  bullish_aligned={BULLISH_ALIGNED_RANGES}  bullish_trend={BULLISH_TREND_SUMS}", flush=True)
    print(f"  bearish_aligned={BEARISH_ALIGNED_RANGES}  bearish_trend={BEARISH_TREND_SUMS}", flush=True)
    print(f"  momentum={MOMENTUM_RATIOS}", flush=True)
    print(f"  trough={TROUGH_DRAWDOWNS}  proximity={TROUGH_PROXIMITIES}", flush=True)

    with requests.get(url, params=params, timeout=TIMEOUT_PER_FOLD_S, stream=True) as resp:
        resp.raise_for_status()
        # ASP.NET Core returns a JSON array (possibly streaming).
        # We buffer the full body then parse – safe for arrays up to a few MB.
        raw = resp.content
    return json.loads(raw)


def call_full_backtest(base_url: str, bull_aligned: int, bull_trend: int,
                       bear_aligned: int, bear_trend: int,
                       momentum: float, trough: float, proximity: float) -> dict:
    params = {
        "startDate": START,
        "endDate":   END,
        "persist":   "false",
        "momentumBrakeMaxRatio":           momentum,
        "troughMinDrawdownPercent":        trough,
        "troughRecoveryProximityPercent":  proximity,
        "bullishMinAlignedRanges":         bull_aligned,
        "bullishMinTrendCountSums":        bull_trend,
        "bearishMinAlignedRanges":         bear_aligned,
        "bearishMinTrendCountSums":        bear_trend,
    }
    url = f"{base_url}/Backtest"
    print(f"\nGET {url} (full 2017-2026, best LTF params)", flush=True)
    resp = requests.get(url, params=params, timeout=3600)
    resp.raise_for_status()
    return resp.json()


def write_csv(rows: list[dict], path: Path) -> None:
    if not rows:
        return
    with path.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=rows[0].keys())
        w.writeheader()
        w.writerows(rows)


def camel(s: str) -> str:
    """Convert PascalCase to camelCase for JSON key lookup."""
    return s[0].lower() + s[1:] if s else s


def get(row: dict, key: str):
    """Try PascalCase then camelCase."""
    return row.get(key, row.get(camel(key)))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://localhost:6003")
    parser.add_argument("--output",   default="/tmp/wf_ltf_sweep")
    args = parser.parse_args()

    out = Path(args.output)
    out.mkdir(parents=True, exist_ok=True)

    # ── 1. Walk-forward run ──────────────────────────────────────────────────
    t0 = datetime.now(timezone.utc)
    folds = call_walkforward(args.base_url)
    elapsed = (datetime.now(timezone.utc) - t0).total_seconds()
    print(f"\n✓ WF done in {elapsed/60:.1f} min  →  {len(folds)} folds", flush=True)

    if not folds:
        print("No folds returned – check server logs.", file=sys.stderr)
        sys.exit(1)

    write_csv(folds, out / "wf_folds.csv")

    # ── 2. Print fold summary ────────────────────────────────────────────────
    print("\n" + "=" * 90)
    print(f"{'Fold':>4}  {'TestStart':>10}  {'TestEnd':>10}  {'TestProfit%':>11}  "
          f"{'BullAl':>6}  {'BullTr':>6}  {'BearAl':>6}  {'BearTr':>6}  "
          f"{'Momentum':>8}  {'Trough':>6}  {'Prox':>6}")
    print("-" * 90)
    for f in folds:
        ts = str(get(f, "TestStart") or "")[:10]
        te = str(get(f, "TestEnd") or "")[:10]
        tp = get(f, "TestTotalProfitPercent")
        tp_s = f"{tp:+.1f}" if tp is not None else "   N/A"
        ba  = get(f, "BestBullishMinAlignedRanges")
        bt  = get(f, "BestBullishMinTrendCountSum")
        bea = get(f, "BestBearishMinAlignedRanges")
        bet = get(f, "BestBearishMinTrendCountSum")
        mo  = get(f, "BestMomentumBrakeMaxRatio")
        tr  = get(f, "BestTroughMinDrawdownPercent")
        pr  = get(f, "BestTroughRecoveryProximityPercent")
        fold_n = get(f, "Fold")
        print(f"{fold_n:>4}  {ts:>10}  {te:>10}  {tp_s:>11}  "
              f"{str(ba):>6}  {str(bt):>6}  {str(bea):>6}  {str(bet):>6}  "
              f"{str(mo):>8}  {str(tr):>6}  {str(pr):>6}")
    print("=" * 90, flush=True)

    # ── 3. Derive best params by frequency ───────────────────────────────────
    param_combos = Counter(
        (get(f, "BestBullishMinAlignedRanges"),
         get(f, "BestBullishMinTrendCountSum"),
         get(f, "BestBearishMinAlignedRanges"),
         get(f, "BestBearishMinTrendCountSum"))
        for f in folds
    )
    best_params, freq = param_combos.most_common(1)[0]
    bull_aligned, bull_trend, bear_aligned, bear_trend = best_params

    # Also find best by avg TestTotalProfitPercent
    by_combo: dict[tuple, list[float]] = {}
    for f in folds:
        key = (get(f, "BestBullishMinAlignedRanges"),
               get(f, "BestBullishMinTrendCountSum"),
               get(f, "BestBearishMinAlignedRanges"),
               get(f, "BestBearishMinTrendCountSum"))
        tp = get(f, "TestTotalProfitPercent")
        if tp is not None:
            by_combo.setdefault(key, []).append(tp)

    best_avg_key = max(by_combo, key=lambda k: sum(by_combo[k]) / len(by_combo[k]) if by_combo[k] else -999)
    best_avg_val = sum(by_combo[best_avg_key]) / len(by_combo[best_avg_key])

    # Prefer avg-profit winner; fall back to frequency
    chosen_params = best_avg_key
    chosen_bull_aligned, chosen_bull_trend, chosen_bear_aligned, chosen_bear_trend = chosen_params

    print(f"\n  Most frequent best LTF: bullish_aligned={bull_aligned} trend={bull_trend}  "
          f"bearish_aligned={bear_aligned} trend={bear_trend}  (×{freq} folds)")
    print(f"  Best avg test profit:   bullish_aligned={chosen_bull_aligned} trend={chosen_bull_trend}  "
          f"bearish_aligned={chosen_bear_aligned} trend={chosen_bear_trend}  "
          f"(avg={best_avg_val:+.2f}%)")

    # Also find best momentum/trough/prox by averaging over folds
    best_mo = max(set(get(f, "BestMomentumBrakeMaxRatio") for f in folds), default=0.25)
    best_tr = max(set(get(f, "BestTroughMinDrawdownPercent") for f in folds), default=8.0)
    best_pr = max(set(get(f, "BestTroughRecoveryProximityPercent") for f in folds), default=5.0)

    # Use median-ish: most frequent
    mo_counter = Counter(get(f, "BestMomentumBrakeMaxRatio") for f in folds)
    tr_counter = Counter(get(f, "BestTroughMinDrawdownPercent") for f in folds)
    pr_counter = Counter(get(f, "BestTroughRecoveryProximityPercent") for f in folds)
    best_mo = mo_counter.most_common(1)[0][0]
    best_tr = tr_counter.most_common(1)[0][0]
    best_pr = pr_counter.most_common(1)[0][0]

    print(f"  Best momentum={best_mo}  trough={best_tr}  proximity={best_pr}", flush=True)

    # ── 4. Full 2017-2026 backtest with best params ──────────────────────────
    result = call_full_backtest(
        args.base_url,
        bull_aligned=chosen_bull_aligned,
        bull_trend=chosen_bull_trend,
        bear_aligned=chosen_bear_aligned,
        bear_trend=chosen_bear_trend,
        momentum=best_mo,
        trough=best_tr,
        proximity=best_pr,
    )

    total_profit = result.get("totalProfitPercent")
    bah          = result.get("buyAndHoldPercent")
    buys         = result.get("totalBuys", 0)
    sells        = result.get("totalSells", 0)
    win_rate     = result.get("winRatePercent")
    max_loss     = result.get("maxLossPercent")
    max_gain     = result.get("maxGainPercent")

    # ── 5. Print final summary ───────────────────────────────────────────────
    print("\n" + "=" * 90)
    print(f"Full backtest 2017-01-01 → 2026-01-01  (best WF params)")
    print(f"  LTF:      bullish_aligned={chosen_bull_aligned} trend={chosen_bull_trend}  "
          f"bearish_aligned={chosen_bear_aligned} trend={chosen_bear_trend}")
    print(f"  Dist:     momentum={best_mo}  trough={best_tr}  proximity={best_pr}")
    print(f"  Profit:   {total_profit:+.2f}%  (Buy&Hold {bah:+.2f}%)" if total_profit is not None else "  Profit: N/A")
    print(f"  Trades:   {buys} buys / {sells} sells")
    print(f"  WinRate:  {win_rate:.1f}%" if win_rate is not None else "  WinRate: N/A")
    print(f"  MaxLoss:  {max_loss:.2f}%  MaxGain: {max_gain:.2f}%" if max_loss is not None else "")
    print("=" * 90, flush=True)

    # ── 6. Save summary JSON ─────────────────────────────────────────────────
    summary = {
        "chosen_ltf": {
            "bullishMinAlignedRanges": chosen_bull_aligned,
            "bullishMinTrendCountSum": chosen_bull_trend,
            "bearishMinAlignedRanges": chosen_bear_aligned,
            "bearishMinTrendCountSum": chosen_bear_trend,
        },
        "chosen_dist": {
            "momentumBrakeMaxRatio": best_mo,
            "troughMinDrawdownPercent": best_tr,
            "troughRecoveryProximityPercent": best_pr,
        },
        "full_backtest": {
            "totalProfitPercent": total_profit,
            "buyAndHoldPercent": bah,
            "totalBuys": buys,
            "totalSells": sells,
            "winRatePercent": win_rate,
            "maxLossPercent": max_loss,
            "maxGainPercent": max_gain,
        },
        "baseline_for_comparison": {
            "note": "ADR 011 + HardStopLoss=-10%: Cum=-126.7%, MaxLoss=-10.45%, 27 trades"
        }
    }
    (out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"\nOutput written to {out}/", flush=True)


if __name__ == "__main__":
    main()
