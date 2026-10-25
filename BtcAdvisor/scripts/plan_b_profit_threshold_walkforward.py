#!/usr/bin/env python3
"""Walk-forward evaluation for Plan B minimum-profit sell threshold.

Rule:
- Open on first buy signal when flat.
- Ignore sell signals until price >= entry_price * (1 + x_pct/100).
- If still open at window end, force-close at last observed price in the window.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any


@dataclass
class Trade:
    timestamp: datetime
    advice: str
    price: float


@dataclass
class WindowResult:
    x_pct: float
    window_start: datetime
    window_end: datetime
    trade_count: int
    closed_deals: int
    forced_close: bool
    strategy_profit_pct: float
    buy_hold_pct: float
    strategy_multiple: float
    buy_hold_multiple: float
    factor_vs_buy_hold: float


def parse_utc(value: str) -> datetime:
    normalized = value.replace("Z", "+00:00")
    # Python's fromisoformat supports up to 6 fractional second digits.
    if "." in normalized and ("+" in normalized[10:] or "-" in normalized[10:]):
        # Split into datetime main and timezone suffix.
        sign_index = normalized.rfind("+")
        if sign_index == -1:
            sign_index = normalized.rfind("-")
        if sign_index > 10:
            main = normalized[:sign_index]
            tz = normalized[sign_index:]
            head, frac = main.split(".", 1)
            frac = (frac + "000000")[:6]
            normalized = f"{head}.{frac}{tz}"
    dt = datetime.fromisoformat(normalized)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(timezone.utc)


def to_iso_utc(dt: datetime) -> str:
    return dt.astimezone(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def is_buy(advice: str) -> bool:
    return "buy" in advice.lower()


def is_sell(advice: str) -> bool:
    lower = advice.lower()
    return "sell" in lower or lower == "hardstoploss"


def build_year_windows(start: datetime, end: datetime) -> list[tuple[datetime, datetime]]:
    windows: list[tuple[datetime, datetime]] = []
    cursor = start
    while cursor < end:
        next_year = cursor + timedelta(days=365)
        window_end = min(next_year, end)
        windows.append((cursor, window_end))
        cursor = window_end
    return windows


def load_trades(backtest_path: Path) -> tuple[datetime, datetime, list[Trade]]:
    payload = json.loads(backtest_path.read_text(encoding="utf-8"))
    start_date = parse_utc(payload["StartDate"])
    end_date = parse_utc(payload["EndDate"])

    trades = [
        Trade(
            timestamp=parse_utc(t["Timestamp"]),
            advice=str(t["TradeAdvice"]),
            price=float(t["Price"]),
        )
        for t in payload.get("Trades", [])
        if t.get("Timestamp") and t.get("TradeAdvice") and t.get("Price") is not None
    ]
    trades.sort(key=lambda t: t.timestamp)
    return start_date, end_date, trades


def simulate_window(window_trades: list[Trade], x_pct: float, window_start: datetime, window_end: datetime) -> WindowResult:
    if not window_trades:
        return WindowResult(
            x_pct=x_pct,
            window_start=window_start,
            window_end=window_end,
            trade_count=0,
            closed_deals=0,
            forced_close=False,
            strategy_profit_pct=math.nan,
            buy_hold_pct=math.nan,
            strategy_multiple=math.nan,
            buy_hold_multiple=math.nan,
            factor_vs_buy_hold=math.nan,
        )

    first_price = window_trades[0].price
    last_price = window_trades[-1].price
    buy_hold_pct = ((last_price / first_price) - 1.0) * 100.0 if first_price > 0 else math.nan

    capital = 1.0
    open_entry: float | None = None
    closed_deals = 0
    forced_close = False

    for t in window_trades:
        if t.price <= 0:
            continue

        if is_buy(t.advice):
            if open_entry is None:
                open_entry = t.price
            continue

        if is_sell(t.advice) and open_entry is not None:
            target = open_entry * (1.0 + x_pct / 100.0)
            if t.price >= target:
                capital *= t.price / open_entry
                open_entry = None
                closed_deals += 1

    if open_entry is not None and last_price > 0:
        capital *= last_price / open_entry
        forced_close = True
        closed_deals += 1

    strategy_profit_pct = (capital - 1.0) * 100.0
    strategy_multiple = capital
    buy_hold_multiple = (1.0 + (buy_hold_pct / 100.0)) if not math.isnan(buy_hold_pct) else math.nan

    if math.isnan(buy_hold_multiple) or buy_hold_multiple <= 0:
        factor_vs_buy_hold = math.nan
    else:
        factor_vs_buy_hold = strategy_multiple / buy_hold_multiple

    return WindowResult(
        x_pct=x_pct,
        window_start=window_start,
        window_end=window_end,
        trade_count=len(window_trades),
        closed_deals=closed_deals,
        forced_close=forced_close,
        strategy_profit_pct=strategy_profit_pct,
        buy_hold_pct=buy_hold_pct,
        strategy_multiple=strategy_multiple,
        buy_hold_multiple=buy_hold_multiple,
        factor_vs_buy_hold=factor_vs_buy_hold,
    )


def write_outputs(results: list[WindowResult], out_dir: Path) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)

    csv_path = out_dir / "plan_b_profit_threshold_walkforward.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow([
            "x_pct",
            "window_start",
            "window_end",
            "trade_count",
            "closed_deals",
            "forced_close",
            "strategy_profit_pct",
            "buy_hold_pct",
            "strategy_multiple",
            "buy_hold_multiple",
            "factor_vs_buy_hold",
        ])
        for r in results:
            w.writerow([
                f"{r.x_pct:.2f}",
                to_iso_utc(r.window_start),
                to_iso_utc(r.window_end),
                r.trade_count,
                r.closed_deals,
                str(r.forced_close).lower(),
                f"{r.strategy_profit_pct:.6f}" if not math.isnan(r.strategy_profit_pct) else "nan",
                f"{r.buy_hold_pct:.6f}" if not math.isnan(r.buy_hold_pct) else "nan",
                f"{r.strategy_multiple:.6f}" if not math.isnan(r.strategy_multiple) else "nan",
                f"{r.buy_hold_multiple:.6f}" if not math.isnan(r.buy_hold_multiple) else "nan",
                f"{r.factor_vs_buy_hold:.6f}" if not math.isnan(r.factor_vs_buy_hold) else "nan",
            ])

    by_x: dict[float, list[WindowResult]] = {}
    for r in results:
        by_x.setdefault(r.x_pct, []).append(r)

    ranking_rows: list[dict[str, Any]] = []
    for x, rows in sorted(by_x.items()):
        valid = [r for r in rows if not math.isnan(r.factor_vs_buy_hold)]
        avg_factor = sum(r.factor_vs_buy_hold for r in valid) / len(valid) if valid else math.nan
        median_factor = sorted(r.factor_vs_buy_hold for r in valid)[len(valid) // 2] if valid else math.nan
        meet_factor_2 = sum(1 for r in valid if r.factor_vs_buy_hold >= 2.0)
        ranking_rows.append(
            {
                "x_pct": x,
                "windows": len(rows),
                "valid_windows": len(valid),
                "avg_factor_vs_buy_hold": avg_factor,
                "median_factor_vs_buy_hold": median_factor,
                "windows_meeting_factor_2": meet_factor_2,
            }
        )

    ranking_rows.sort(
        key=lambda r: (
            -(r["windows_meeting_factor_2"]),
            -(r["avg_factor_vs_buy_hold"] if not math.isnan(r["avg_factor_vs_buy_hold"]) else -1e9),
        )
    )

    md_path = out_dir / "plan_b_profit_threshold_ranking.md"
    lines = [
        "# Plan B Walk-Forward Ranking (Mindest-Profit-Exit)",
        "",
        "Regel: Nach Kauf werden Sell-Signale ignoriert, bis Preis >= Einstieg * (1 + X%).",
        "",
        "## Ranking",
        "",
        "| X % | Fenster | Gueltige Fenster | Avg Faktor vs Buy&Hold | Median Faktor vs Buy&Hold | Fenster mit Faktor >= 2 |",
        "|---:|---:|---:|---:|---:|---:|",
    ]
    for row in ranking_rows:
        avg_factor = "nan" if math.isnan(row["avg_factor_vs_buy_hold"]) else f"{row['avg_factor_vs_buy_hold']:.4f}"
        med_factor = "nan" if math.isnan(row["median_factor_vs_buy_hold"]) else f"{row['median_factor_vs_buy_hold']:.4f}"
        lines.append(
            f"| {row['x_pct']:.2f} | {row['windows']} | {row['valid_windows']} | {avg_factor} | {med_factor} | {row['windows_meeting_factor_2']} |"
        )

    lines.append("")
    lines.append("## Dateien")
    lines.append("")
    lines.append(f"- CSV: {csv_path}")
    lines.append(f"- Markdown: {md_path}")

    md_path.write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--backtest-json",
        default="BtcAdvisor/backtest_2017_now_direct.json",
        help="Path to Backtest JSON containing full trade list.",
    )
    parser.add_argument(
        "--x-list",
        default="25,50",
        help="Comma-separated list of minimum profit thresholds in percent.",
    )
    parser.add_argument(
        "--output-dir",
        default="BtcAdvisor/data/plan_b_profit_threshold_walkforward",
    )
    args = parser.parse_args()

    x_values = [float(x.strip()) for x in args.x_list.split(",") if x.strip()]
    backtest_path = Path(args.backtest_json)

    start_date, end_date, trades = load_trades(backtest_path)
    windows = build_year_windows(start_date, end_date)

    results: list[WindowResult] = []
    for x in x_values:
        for ws, we in windows:
            window_trades = [t for t in trades if ws <= t.timestamp <= we]
            results.append(simulate_window(window_trades, x, ws, we))

    out_dir = Path(args.output_dir)
    write_outputs(results, out_dir)

    print(f"Wrote: {out_dir / 'plan_b_profit_threshold_walkforward.csv'}")
    print(f"Wrote: {out_dir / 'plan_b_profit_threshold_ranking.md'}")


if __name__ == "__main__":
    main()
