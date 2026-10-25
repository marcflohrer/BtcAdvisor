#!/usr/bin/env python3
"""Yearly backtest profit aggregator.

Läuft einen Backtest 2017-01-01 → heute (oder konfigurierbarer Zeitraum) gegen
die laufende API und gruppiert die Sell-Trades nach Kalenderjahr. Output: CSV +
Console-Tabelle mit Profit, Win-Rate, Max-Loss, Max-Gain, Trade-Anzahl pro Jahr.

Kein C#-Server-Code wird verändert. Default-Konfiguration des Workers gilt
(EnableScoreBonus=false, EnableAtrStopLoss=false unless overridden in
appsettings).

Beispiel:
  python3 yearly_profit_breakdown.py
  python3 yearly_profit_breakdown.py --start 2018-01-01 --end 2025-12-31
  python3 yearly_profit_breakdown.py --base-url http://localhost:6003 --output yearly.csv
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import json
import sys
import urllib.parse
import urllib.request
from collections import defaultdict
from typing import Any


def parse_args() -> argparse.Namespace:
    today = dt.datetime.now(dt.timezone.utc).date()
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--base-url", default="http://localhost:6003", help="API base URL")
    p.add_argument("--start", default="2017-01-01", help="Startdatum (YYYY-MM-DD)")
    p.add_argument("--end", default=today.isoformat(), help="Enddatum (YYYY-MM-DD)")
    p.add_argument("--persist", action="store_true", help="Wenn gesetzt: Signale als ActionLogs persistieren")
    p.add_argument("--timeout", type=int, default=7200, help="HTTP-Timeout in Sekunden (default 7200 = 2h)")
    p.add_argument("--output", default=None, help="Optionaler CSV-Output-Pfad")
    # Optional override of Confluence / Score gates (None → server default)
    p.add_argument("--bullish-min-aligned-ranges", type=int, default=None)
    p.add_argument("--bullish-min-trend-count-sum", type=int, default=None)
    p.add_argument("--bearish-min-aligned-ranges", type=int, default=None)
    p.add_argument("--bearish-min-trend-count-sum", type=int, default=None)
    p.add_argument("--bearish-regime-min-linear-bullish-score", type=float, default=None)
    p.add_argument("--momentum-brake-max-ratio", type=float, default=None)
    p.add_argument("--trough-min-drawdown-percent", type=float, default=None)
    p.add_argument("--trough-recovery-proximity-percent", type=float, default=None)
    return p.parse_args()


def iso_utc(date_str: str) -> str:
    # API erwartet ISO 8601 mit Z-Suffix
    d = dt.date.fromisoformat(date_str)
    return f"{d.isoformat()}T00:00:00Z"


def call_backtest(base_url: str, start: str, end: str, persist: bool, timeout: int, overrides: dict[str, Any] | None = None) -> dict[str, Any]:
    params: dict[str, Any] = {
        "startDate": iso_utc(start),
        "endDate": iso_utc(end),
        "persist": "true" if persist else "false",
    }
    if overrides:
        params.update({k: v for k, v in overrides.items() if v is not None})
    qs = urllib.parse.urlencode(params)
    url = f"{base_url.rstrip('/')}/Backtest?{qs}"
    print(f"GET {url}", file=sys.stderr)
    req = urllib.request.Request(url, headers={"Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        if resp.status != 200:
            raise SystemExit(f"Backtest-API HTTP {resp.status}: {resp.read()!r}")
        return json.loads(resp.read())


def aggregate_by_year(summary: dict[str, Any]) -> list[dict[str, Any]]:
    trades = summary.get("trades") or []
    sells_by_year: dict[int, list[dict[str, Any]]] = defaultdict(list)
    buys_by_year: dict[int, int] = defaultdict(int)

    for t in trades:
        ts = t.get("timestamp")
        if not ts:
            continue
        year = dt.datetime.fromisoformat(ts.replace("Z", "+00:00")).year
        advice = (t.get("tradeAdvice") or "").lower()
        if t.get("profitPercent") is not None:
            sells_by_year[year].append(t)
        elif "buy" in advice:
            buys_by_year[year] += 1

    rows: list[dict[str, Any]] = []
    cum = 0.0
    for year in sorted(set(list(sells_by_year.keys()) + list(buys_by_year.keys()))):
        sells = sells_by_year[year]
        n_sells = len(sells)
        profits = [s["profitPercent"] for s in sells if s.get("profitPercent") is not None]
        total = round(sum(profits), 2) if profits else 0.0
        cum += total
        wins = sum(1 for p in profits if p > 0)
        win_rate = round(100.0 * wins / n_sells, 1) if n_sells else None
        max_gain = round(max(profits), 2) if profits else None
        max_loss = round(min(profits), 2) if profits else None
        rows.append({
            "year": year,
            "buys": buys_by_year[year],
            "sells": n_sells,
            "profit_pct": total,
            "cum_profit_pct": round(cum, 2),
            "win_rate_pct": win_rate,
            "max_gain_pct": max_gain,
            "max_loss_pct": max_loss,
        })
    return rows


def render_console(summary: dict[str, Any], rows: list[dict[str, Any]]) -> None:
    print()
    print("=" * 92)
    print(f"Backtest {summary.get('startDate')} → {summary.get('endDate')}")
    print(f"  TotalProfit:   {summary.get('totalProfitPercent')} %")
    print(f"  Buy&Hold:      {summary.get('buyAndHoldPercent')} %")
    print(f"  WinRate:       {summary.get('winRatePercent')} %")
    print(f"  MaxLoss/Gain:  {summary.get('maxLossPercent')} % / {summary.get('maxGainPercent')} %")
    print(f"  Buys/Sells:    {summary.get('totalBuys')} / {summary.get('totalSells')}")
    print("=" * 92)
    header = f"{'Jahr':>6} {'Buys':>5} {'Sells':>6} {'Profit %':>10} {'Cum %':>10} {'Win %':>7} {'MaxGain':>9} {'MaxLoss':>9}"
    print(header)
    print("-" * len(header))
    for r in rows:
        print(
            f"{r['year']:>6} {r['buys']:>5} {r['sells']:>6} "
            f"{r['profit_pct']:>10.2f} {r['cum_profit_pct']:>10.2f} "
            f"{(r['win_rate_pct'] if r['win_rate_pct'] is not None else 0):>7.1f} "
            f"{(r['max_gain_pct'] if r['max_gain_pct'] is not None else 0):>9.2f} "
            f"{(r['max_loss_pct'] if r['max_loss_pct'] is not None else 0):>9.2f}"
        )
    print("=" * 92)


def write_csv(rows: list[dict[str, Any]], path: str) -> None:
    fieldnames = ["year", "buys", "sells", "profit_pct", "cum_profit_pct", "win_rate_pct", "max_gain_pct", "max_loss_pct"]
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames)
        w.writeheader()
        w.writerows(rows)
    print(f"CSV geschrieben: {path}", file=sys.stderr)


def main() -> int:
    args = parse_args()
    overrides = {
        "bullishMinAlignedRanges": args.bullish_min_aligned_ranges,
        "bullishMinTrendCountSum": args.bullish_min_trend_count_sum,
        "bearishMinAlignedRanges": args.bearish_min_aligned_ranges,
        "bearishMinTrendCountSum": args.bearish_min_trend_count_sum,
        "bearishRegimeMinLinearBullishScore": args.bearish_regime_min_linear_bullish_score,
        "momentumBrakeMaxRatio": args.momentum_brake_max_ratio,
        "troughMinDrawdownPercent": args.trough_min_drawdown_percent,
        "troughRecoveryProximityPercent": args.trough_recovery_proximity_percent,
    }
    summary = call_backtest(args.base_url, args.start, args.end, args.persist, args.timeout, overrides)
    rows = aggregate_by_year(summary)
    render_console(summary, rows)
    if args.output:
        write_csv(rows, args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
