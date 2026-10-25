#!/usr/bin/env python3
"""Validate whether ADX at buy-time helps separate losing vs winning closed deals.

This script uses existing API endpoints:
- /Backtest for closed trade outcomes
- /indicatorresults for ADX snapshots around buy timestamps

Output:
- JSON summary with statistical tests and threshold sweep
- CSV with per-deal enriched observations
"""

from __future__ import annotations

import argparse
import bisect
import csv
import json
import math
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.parse import urlencode
from urllib.request import Request, urlopen


def clamp(value: float, minimum: float = 0.0, maximum: float = 1.0) -> float:
    return max(minimum, min(maximum, value))


def percentile_linear(values: list[float], q: float) -> float:
    if not values:
        return math.nan
    q = clamp(q, 0.0, 1.0)
    s = sorted(values)
    if len(s) == 1:
        return s[0]

    pos = q * (len(s) - 1)
    lo = int(math.floor(pos))
    hi = int(math.ceil(pos))
    if lo == hi:
        return s[lo]
    weight = pos - lo
    return s[lo] * (1.0 - weight) + s[hi] * weight


def parse_utc(value: str) -> datetime:
    normalized = value.replace("Z", "+00:00")
    dt = datetime.fromisoformat(normalized)
    if dt.tzinfo is None:
        return dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(timezone.utc)


def to_iso_utc(dt: datetime) -> str:
    return dt.astimezone(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def interval_to_ms(interval: str) -> int:
    unit = interval[-1]
    value = int(interval[:-1])
    if unit == "m":
        return value * 60_000
    if unit == "h":
        return value * 60 * 60_000
    if unit == "d":
        return value * 24 * 60 * 60_000
    raise ValueError(f"Unsupported interval: {interval}")


def is_buy(advice: str) -> bool:
    return "buy" in advice.lower()


def is_sell(advice: str) -> bool:
    lower = advice.lower()
    return "sell" in lower or lower == "hardstoploss"


def ci_get(mapping: dict[str, Any], key: str) -> Any:
    for candidate, value in mapping.items():
        if candidate.lower() == key.lower():
            return value
    raise KeyError(key)


def ci_get_optional(mapping: dict[str, Any], key: str, default: Any = None) -> Any:
    for candidate, value in mapping.items():
        if candidate.lower() == key.lower():
            return value
    return default


class ApiError(RuntimeError):
    pass


class ApiClient:
    def __init__(self, base_url: str, timeout_seconds: int, retries: int, retry_sleep_seconds: float) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout_seconds = timeout_seconds
        self.retries = retries
        self.retry_sleep_seconds = retry_sleep_seconds

    def get_json(self, path: str, params: dict[str, Any]) -> Any:
        query = urlencode(params)
        url = f"{self.base_url}{path}?{query}"

        last_error: Exception | None = None
        for attempt in range(1, self.retries + 1):
            try:
                request = Request(url, headers={"Accept": "application/json"})
                with urlopen(request, timeout=self.timeout_seconds) as response:
                    payload = response.read().decode("utf-8")
                return json.loads(payload)
            except Exception as exc:  # noqa: BLE001
                last_error = exc
                if attempt < self.retries:
                    time.sleep(self.retry_sleep_seconds * attempt)

        raise ApiError(f"GET failed for {url}: {last_error}")


def fetch_binance_klines(
    interval: str,
    start: datetime,
    end: datetime,
    timeout_seconds: int,
    retries: int,
    retry_sleep_seconds: float,
) -> list[list[Any]]:
    base_url = "https://api.binance.com/api/v3/klines"
    start_ms = int(start.timestamp() * 1000)
    end_ms = int(end.timestamp() * 1000)
    step_ms = interval_to_ms(interval)

    cursor = start_ms
    out: list[list[Any]] = []

    while cursor <= end_ms:
        params = urlencode(
            {
                "symbol": "BTCUSDT",
                "interval": interval,
                "startTime": cursor,
                "endTime": end_ms,
                "limit": 1000,
            }
        )
        url = f"{base_url}?{params}"

        payload = None
        last_error: Exception | None = None
        for attempt in range(1, retries + 1):
            try:
                req = Request(url, headers={"Accept": "application/json"})
                with urlopen(req, timeout=timeout_seconds) as response:
                    payload = json.loads(response.read().decode("utf-8"))
                break
            except Exception as exc:  # noqa: BLE001
                last_error = exc
                if attempt < retries:
                    time.sleep(retry_sleep_seconds * attempt)

        if payload is None:
            raise ApiError(f"Binance klines failed for {url}: {last_error}")

        if not payload:
            break

        out.extend(payload)
        last_open_time = int(payload[-1][0])
        next_cursor = last_open_time + step_ms
        if next_cursor <= cursor:
            break
        cursor = next_cursor
        time.sleep(0.05)

    return out


def compute_adx_series(highs: list[float], lows: list[float], closes: list[float], period: int) -> list[float | None]:
    count = len(closes)
    adx: list[float | None] = [None] * count
    if period < 2 or count == 0 or len(highs) != count or len(lows) != count:
        return adx

    tr = [0.0] * count
    plus_dm = [0.0] * count
    minus_dm = [0.0] * count

    for i in range(1, count):
        high_diff = highs[i] - highs[i - 1]
        low_diff = lows[i - 1] - lows[i]
        plus_dm[i] = high_diff if (high_diff > low_diff and high_diff > 0.0) else 0.0
        minus_dm[i] = low_diff if (low_diff > high_diff and low_diff > 0.0) else 0.0

        high_low = highs[i] - lows[i]
        high_close = abs(highs[i] - closes[i - 1])
        low_close = abs(lows[i] - closes[i - 1])
        tr[i] = max(high_low, high_close, low_close)

    if count <= period:
        return adx

    tr_smooth = sum(tr[1 : period + 1])
    plus_smooth = sum(plus_dm[1 : period + 1])
    minus_smooth = sum(minus_dm[1 : period + 1])

    dx: list[float | None] = [None] * count
    for i in range(period, count):
        if i > period:
            tr_smooth = tr_smooth - (tr_smooth / period) + tr[i]
            plus_smooth = plus_smooth - (plus_smooth / period) + plus_dm[i]
            minus_smooth = minus_smooth - (minus_smooth / period) + minus_dm[i]

        if tr_smooth <= 0.0:
            dx[i] = 0.0
            continue

        plus_di = 100.0 * (plus_smooth / tr_smooth)
        minus_di = 100.0 * (minus_smooth / tr_smooth)
        di_sum = plus_di + minus_di
        dx[i] = 0.0 if di_sum == 0.0 else 100.0 * (abs(plus_di - minus_di) / di_sum)

    first_adx_index = (period * 2) - 1
    if first_adx_index >= count:
        return adx

    initial_dx = [v for v in dx[period : first_adx_index + 1] if v is not None]
    if len(initial_dx) != period:
        return adx

    adx[first_adx_index] = sum(initial_dx) / period
    for i in range(first_adx_index + 1, count):
        if dx[i] is None or adx[i - 1] is None:
            continue
        adx[i] = ((adx[i - 1] * (period - 1)) + dx[i]) / period

    return adx


def compute_ema_series(values: list[float], period: int) -> list[float | None]:
    out: list[float | None] = [None] * len(values)
    if period < 1 or len(values) < period:
        return out

    seed = sum(values[:period]) / period
    out[period - 1] = seed
    alpha = 2.0 / (period + 1)

    ema_prev = seed
    for i in range(period, len(values)):
        ema_prev = (values[i] - ema_prev) * alpha + ema_prev
        out[i] = ema_prev

    return out


def compute_rsi_series(values: list[float], period: int) -> list[float | None]:
    out: list[float | None] = [None] * len(values)
    if period < 1 or len(values) <= period:
        return out

    gains: list[float] = [0.0] * len(values)
    losses: list[float] = [0.0] * len(values)
    for i in range(1, len(values)):
        delta = values[i] - values[i - 1]
        gains[i] = max(delta, 0.0)
        losses[i] = max(-delta, 0.0)

    avg_gain = sum(gains[1 : period + 1]) / period
    avg_loss = sum(losses[1 : period + 1]) / period

    def to_rsi(gain: float, loss: float) -> float:
        if loss == 0.0:
            return 100.0
        rs = gain / loss
        return 100.0 - (100.0 / (1.0 + rs))

    out[period] = to_rsi(avg_gain, avg_loss)
    for i in range(period + 1, len(values)):
        avg_gain = ((avg_gain * (period - 1)) + gains[i]) / period
        avg_loss = ((avg_loss * (period - 1)) + losses[i]) / period
        out[i] = to_rsi(avg_gain, avg_loss)

    return out


def compute_atr_series(highs: list[float], lows: list[float], closes: list[float], period: int) -> list[float | None]:
    out: list[float | None] = [None] * len(closes)
    if period < 1 or len(closes) <= period:
        return out

    tr = [0.0] * len(closes)
    for i in range(1, len(closes)):
        high_low = highs[i] - lows[i]
        high_close = abs(highs[i] - closes[i - 1])
        low_close = abs(lows[i] - closes[i - 1])
        tr[i] = max(high_low, high_close, low_close)

    atr = sum(tr[1 : period + 1]) / period
    out[period] = atr
    for i in range(period + 1, len(closes)):
        atr = ((atr * (period - 1)) + tr[i]) / period
        out[i] = atr

    return out


def compute_fib_retracement_series(
    highs: list[float],
    lows: list[float],
    lookback_bars: int,
    retracement_level: float,
) -> list[float | None]:
    out: list[float | None] = [None] * len(highs)
    if lookback_bars < 2 or len(highs) != len(lows):
        return out

    for i in range(len(highs)):
        start = max(0, i - lookback_bars + 1)
        window_high = max(highs[start : i + 1])
        window_low = min(lows[start : i + 1])
        span = window_high - window_low
        if span <= 0:
            continue
        out[i] = window_high - (span * retracement_level)

    return out


def compute_fib_retracement_series_swing(
    highs: list[float],
    lows: list[float],
    left_bars: int,
    right_bars: int,
    retracement_level: float,
) -> list[float | None]:
    out: list[float | None] = [None] * len(highs)
    if len(highs) != len(lows) or left_bars < 1 or right_bars < 1:
        return out

    last_swing_high: float | None = None
    last_swing_low: float | None = None

    n = len(highs)
    for i in range(n):
        pivot_idx = i - right_bars
        if pivot_idx >= left_bars and pivot_idx + right_bars < n:
            start = pivot_idx - left_bars
            end = pivot_idx + right_bars + 1

            high_slice = highs[start:end]
            low_slice = lows[start:end]

            pivot_high = highs[pivot_idx]
            pivot_low = lows[pivot_idx]

            if pivot_high == max(high_slice):
                last_swing_high = pivot_high
            if pivot_low == min(low_slice):
                last_swing_low = pivot_low

        if last_swing_high is not None and last_swing_low is not None:
            swing_high = max(last_swing_high, last_swing_low)
            swing_low = min(last_swing_high, last_swing_low)
            span = swing_high - swing_low
            if span > 0:
                out[i] = swing_high - (span * retracement_level)

    return out


@dataclass
class ClosedDeal:
    buy_timestamp: datetime
    sell_timestamp: datetime
    buy_advice: str
    sell_advice: str
    buy_price: float
    sell_price: float
    profit_percent: float


@dataclass
class EnrichedDeal:
    buy_timestamp: datetime
    sell_timestamp: datetime
    buy_advice: str
    sell_advice: str
    buy_price: float
    sell_price: float
    profit_percent: float
    is_loss: bool
    adx: float
    rsi: float
    ema200: float
    close_at_buy: float
    atr_percent: float
    ema200_slope_bars: float
    fib_retracement_price: float
    fib_distance_percent: float
    volume_rel_20: float = 0.0
    volume_zscore_20: float = 0.0
    gatekeeper_score_v2: float = 0.0
    gatekeeper_threshold_v2: float = 0.0
    gatekeeper_pass_v2: bool = False


def extract_closed_deals(backtest_summary: dict[str, Any]) -> list[ClosedDeal]:
    trades = ci_get_optional(backtest_summary, "trades", [])
    if not isinstance(trades, list):
        raise ValueError("Unexpected backtest response: 'trades' is not a list")

    normalized = sorted(
        trades,
        key=lambda item: parse_utc(str(ci_get_optional(item, "timestamp"))),
    )

    deals: list[ClosedDeal] = []
    open_buy: dict[str, Any] | None = None

    for trade in normalized:
        advice = str(ci_get_optional(trade, "tradeAdvice", ""))
        timestamp = parse_utc(str(ci_get_optional(trade, "timestamp")))
        price = float(ci_get_optional(trade, "price", 0.0))

        if is_buy(advice):
            open_buy = {
                "timestamp": timestamp,
                "advice": advice,
                "price": price,
            }
            continue

        if not is_sell(advice) or open_buy is None:
            continue

        profit_percent = ci_get_optional(trade, "profitPercent")
        if profit_percent is None:
            if open_buy["price"] <= 0:
                continue
            profit_percent = ((price - open_buy["price"]) / open_buy["price"]) * 100.0

        deals.append(
            ClosedDeal(
                buy_timestamp=open_buy["timestamp"],
                sell_timestamp=timestamp,
                buy_advice=str(open_buy["advice"]),
                sell_advice=advice,
                buy_price=float(open_buy["price"]),
                sell_price=price,
                profit_percent=float(profit_percent),
            )
        )
        open_buy = None

    return deals


def extract_adx_value(indicator_payload: dict[str, Any]) -> float:
    current = ci_get(indicator_payload, "current")
    if not isinstance(current, dict):
        raise ValueError("indicatorresults payload has no valid 'current' map")

    for _, result_list in current.items():
        if not isinstance(result_list, list):
            continue
        for row in result_list:
            if not isinstance(row, dict):
                continue
            for key, value in row.items():
                if key.lower() == "value" and value is not None:
                    return float(value)

    raise ValueError("No ADX 'value' found in indicatorresults payload")


def rankdata_average_ties(values: list[float]) -> tuple[list[float], dict[float, int]]:
    indexed = sorted(enumerate(values), key=lambda x: x[1])
    ranks = [0.0] * len(values)
    tie_counts: dict[float, int] = {}

    i = 0
    while i < len(indexed):
        j = i + 1
        while j < len(indexed) and indexed[j][1] == indexed[i][1]:
            j += 1

        avg_rank = (i + 1 + j) / 2.0
        tie_size = j - i
        value = indexed[i][1]
        tie_counts[value] = tie_size

        for k in range(i, j):
            original_idx = indexed[k][0]
            ranks[original_idx] = avg_rank

        i = j

    return ranks, tie_counts


def normal_cdf(x: float) -> float:
    return 0.5 * (1.0 + math.erf(x / math.sqrt(2.0)))


def mann_whitney_u_test(sample_a: list[float], sample_b: list[float]) -> dict[str, float]:
    if not sample_a or not sample_b:
        return {"u": math.nan, "z": math.nan, "p_value": math.nan}

    combined = sample_a + sample_b
    ranks, tie_counts = rankdata_average_ties(combined)

    n1 = len(sample_a)
    n2 = len(sample_b)

    rank_sum_a = sum(ranks[:n1])
    u1 = rank_sum_a - (n1 * (n1 + 1) / 2.0)

    mu = (n1 * n2) / 2.0

    tie_term = sum((t ** 3) - t for t in tie_counts.values())
    tie_correction = tie_term / (12.0 * (n1 + n2) * (n1 + n2 - 1)) if (n1 + n2) > 1 else 0.0
    sigma_sq = (n1 * n2 * (n1 + n2 + 1) / 12.0) - (n1 * n2 * tie_correction)

    if sigma_sq <= 0:
        return {"u": u1, "z": 0.0, "p_value": 1.0}

    sigma = math.sqrt(sigma_sq)
    correction = 0.5 if u1 > mu else -0.5
    z = (u1 - mu - correction) / sigma
    p_value = 2.0 * (1.0 - normal_cdf(abs(z)))

    return {"u": u1, "z": z, "p_value": p_value}


def proportion_z_test(successes: int, total: int, p0: float) -> dict[str, float]:
    if total <= 0:
        return {"z": math.nan, "p_value": math.nan}
    p0 = max(0.0, min(1.0, p0))
    variance = p0 * (1.0 - p0) / total
    if variance <= 0.0:
        return {"z": 0.0, "p_value": 1.0}

    observed = successes / total
    z = (observed - p0) / math.sqrt(variance)
    p_value = 2.0 * (1.0 - normal_cdf(abs(z)))
    return {"z": z, "p_value": p_value}


def median(values: list[float]) -> float:
    if not values:
        return math.nan
    s = sorted(values)
    n = len(s)
    mid = n // 2
    if n % 2 == 0:
        return (s[mid - 1] + s[mid]) / 2.0
    return s[mid]


def summarize_thresholds(deals: list[EnrichedDeal], thresholds: list[float]) -> list[dict[str, float]]:
    baseline_winrate = 100.0 * sum(1 for d in deals if not d.is_loss) / len(deals)
    rows: list[dict[str, float]] = []

    for threshold in thresholds:
        kept = [d for d in deals if d.adx >= threshold]
        if not kept:
            rows.append(
                {
                    "threshold": threshold,
                    "coverage": 0.0,
                    "count": 0,
                    "winrate_percent": math.nan,
                    "lossrate_percent": math.nan,
                    "winrate_delta_pp": math.nan,
                    "mean_profit_percent": math.nan,
                    "total_profit_percent": math.nan,
                }
            )
            continue

        wins = sum(1 for d in kept if not d.is_loss)
        winrate = 100.0 * wins / len(kept)
        lossrate = 100.0 - winrate

        rows.append(
            {
                "threshold": threshold,
                "coverage": len(kept) / len(deals),
                "count": len(kept),
                "winrate_percent": winrate,
                "lossrate_percent": lossrate,
                "winrate_delta_pp": winrate - baseline_winrate,
                "mean_profit_percent": sum(d.profit_percent for d in kept) / len(kept),
                "total_profit_percent": sum(d.profit_percent for d in kept),
            }
        )

    return rows


def summarize_named_gates(
    deals: list[EnrichedDeal],
    gates: dict[str, Any],
    min_coverage_for_claim: float,
    min_effect_pp_for_claim: float,
) -> list[dict[str, float]]:
    baseline_winrate = 100.0 * sum(1 for d in deals if not d.is_loss) / len(deals)
    baseline_winrate_ratio = baseline_winrate / 100.0
    rows: list[dict[str, float]] = []

    for gate_name, predicate in gates.items():
        kept = [d for d in deals if predicate(d)]
        if not kept:
            rows.append(
                {
                    "gate": gate_name,
                    "coverage": 0.0,
                    "count": 0,
                    "winrate_percent": math.nan,
                    "lossrate_percent": math.nan,
                    "winrate_delta_pp": math.nan,
                    "mean_profit_percent": math.nan,
                    "total_profit_percent": math.nan,
                }
            )
            continue

        wins = sum(1 for d in kept if not d.is_loss)
        winrate = 100.0 * wins / len(kept)
        ztest = proportion_z_test(wins, len(kept), baseline_winrate_ratio)
        winrate_delta_pp = winrate - baseline_winrate
        coverage = len(kept) / len(deals)
        meets_significance = (not math.isnan(ztest["p_value"])) and ztest["p_value"] < 0.05
        meets_effect = (not math.isnan(winrate_delta_pp)) and winrate_delta_pp >= min_effect_pp_for_claim
        meets_coverage = coverage >= min_coverage_for_claim
        rows.append(
            {
                "gate": gate_name,
                "coverage": coverage,
                "count": len(kept),
                "winrate_percent": winrate,
                "lossrate_percent": 100.0 - winrate,
                "winrate_delta_pp": winrate_delta_pp,
                "mean_profit_percent": sum(d.profit_percent for d in kept) / len(kept),
                "total_profit_percent": sum(d.profit_percent for d in kept),
                "z_vs_baseline_winrate": ztest["z"],
                "p_value_vs_baseline_winrate": ztest["p_value"],
                "meets_significance": meets_significance,
                "meets_effect": meets_effect,
                "meets_coverage": meets_coverage,
                "gatekeeper_claim_valid": meets_significance and meets_effect and meets_coverage,
            }
        )

    return sorted(rows, key=lambda r: (-(r["winrate_delta_pp"] if not math.isnan(r["winrate_delta_pp"]) else -1e9), -r["coverage"]))


def build_gatekeeper_v2_scores(
    deals: list[EnrichedDeal],
    score_window: int,
    score_quantile: float,
    fallback_threshold: float,
    adx_floor: float,
    fib_max_dist_pct: float,
    mandatory_min_volume_rel: float,
) -> None:
    deals.sort(key=lambda d: d.buy_timestamp)

    trailing_scores: list[float] = []
    for deal in deals:
        has_core_inputs = (
            not math.isnan(deal.adx)
            and not math.isnan(deal.rsi)
            and not math.isnan(deal.ema200)
            and not math.isnan(deal.ema200_slope_bars)
            and deal.volume_rel_20 is not None
            and deal.volume_zscore_20 is not None
        )

        mandatory_filter = (
            has_core_inputs
            and deal.close_at_buy > deal.ema200
            and deal.ema200_slope_bars > 0.0
            and deal.volume_rel_20 >= mandatory_min_volume_rel
        )

        adx_component = clamp((deal.adx - adx_floor) / 25.0) if not math.isnan(deal.adx) else 0.0

        # Prefer RSI in a neutral-bullish zone to avoid catching extreme downtrends.
        if math.isnan(deal.rsi):
            rsi_component = 0.0
        elif deal.rsi <= 40.0:
            rsi_component = clamp((deal.rsi - 20.0) / 20.0)
        elif deal.rsi <= 60.0:
            rsi_component = 1.0
        else:
            rsi_component = clamp((75.0 - deal.rsi) / 15.0)

        if math.isnan(deal.fib_distance_percent):
            fib_component = 0.0
        else:
            fib_component = clamp(1.0 - (deal.fib_distance_percent / max(0.0001, fib_max_dist_pct)))

        volume_component = (
            clamp((deal.volume_zscore_20 + 1.0) / 3.0) if not math.isnan(deal.volume_zscore_20) else 0.0
        )

        score = (
            0.35 * adx_component
            + 0.30 * fib_component
            + 0.20 * rsi_component
            + 0.15 * volume_component
        )

        if len(trailing_scores) >= max(5, score_window // 4):
            history = trailing_scores[-score_window:] if score_window > 0 else trailing_scores
            dynamic_threshold = percentile_linear(history, score_quantile)
        else:
            dynamic_threshold = fallback_threshold

        deal.gatekeeper_score_v2 = score
        deal.gatekeeper_threshold_v2 = dynamic_threshold
        deal.gatekeeper_pass_v2 = mandatory_filter and (score >= dynamic_threshold)

        trailing_scores.append(score)


def write_csv(path: Path, deals: list[EnrichedDeal]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(
            [
                "buy_timestamp",
                "sell_timestamp",
                "buy_advice",
                "sell_advice",
                "buy_price",
                "sell_price",
                "profit_percent",
                "is_loss",
                "adx",
                "rsi",
                "ema200",
                "close_at_buy",
                "atr_percent",
                "ema200_slope_bars",
                "fib_retracement_price",
                "fib_distance_percent",
                "volume_rel_20",
                "volume_zscore_20",
                "gatekeeper_score_v2",
                "gatekeeper_threshold_v2",
                "gatekeeper_pass_v2",
            ]
        )
        for d in deals:
            writer.writerow(
                [
                    to_iso_utc(d.buy_timestamp),
                    to_iso_utc(d.sell_timestamp),
                    d.buy_advice,
                    d.sell_advice,
                    f"{d.buy_price:.8f}",
                    f"{d.sell_price:.8f}",
                    f"{d.profit_percent:.8f}",
                    str(d.is_loss).lower(),
                    f"{d.adx:.8f}",
                    f"{d.rsi:.8f}",
                    f"{d.ema200:.8f}",
                    f"{d.close_at_buy:.8f}",
                    f"{d.atr_percent:.8f}",
                    f"{d.ema200_slope_bars:.8f}",
                    f"{d.fib_retracement_price:.8f}",
                    f"{d.fib_distance_percent:.8f}",
                    f"{d.volume_rel_20:.8f}",
                    f"{d.volume_zscore_20:.8f}",
                    f"{d.gatekeeper_score_v2:.8f}",
                    f"{d.gatekeeper_threshold_v2:.8f}",
                    str(d.gatekeeper_pass_v2).lower(),
                ]
            )


def main() -> None:
    parser = argparse.ArgumentParser(description="Validate ADX predictive value for loss vs win deals")
    parser.add_argument("--base-url", default="http://localhost:6003", help="API base URL")
    parser.add_argument(
        "--backtest-json",
        default="",
        help="Optional path to a backtest JSON file (uses file instead of /Backtest API).",
    )
    parser.add_argument("--start-date", required=True, help="ISO datetime in UTC, e.g. 2017-08-17T00:00:00Z")
    parser.add_argument("--end-date", required=True, help="ISO datetime in UTC")
    parser.add_argument("--time-span", default="15m", help="Indicator timespan for ADX lookup")
    parser.add_argument("--adx-period", type=int, default=14, help="ADX period for indicatorresults endpoint")
    parser.add_argument(
        "--adx-source",
        choices=["indicatorresults", "binance-klines"],
        default="binance-klines",
        help="Source for ADX values; binance-klines is more robust when DB endpoint is unstable.",
    )
    parser.add_argument(
        "--binance-interval",
        default="15m",
        help="Binance kline interval used for ADX computation when --adx-source=binance-klines",
    )
    parser.add_argument("--include-hard-stop-loss", action="store_true", help="Include HardStopLoss closed deals")
    parser.add_argument("--timeout-seconds", type=int, default=180)
    parser.add_argument("--retries", type=int, default=3)
    parser.add_argument("--retry-sleep-seconds", type=float, default=1.0)
    parser.add_argument("--output-dir", default="BtcAdvisor/data/adx_validation")
    parser.add_argument(
        "--thresholds",
        default="20,25,30,35,40,45,50",
        help="Comma-separated ADX thresholds for sweep",
    )
    parser.add_argument(
        "--max-missing-adx-share",
        type=float,
        default=0.40,
        help="Maximum allowed share of closed deals without resolvable ADX before failing.",
    )
    parser.add_argument("--rsi-period", type=int, default=14)
    parser.add_argument("--ema-period", type=int, default=200)
    parser.add_argument("--atr-period", type=int, default=14)
    parser.add_argument("--ema-slope-bars", type=int, default=10)
    parser.add_argument("--rsi-min", type=float, default=40.0)
    parser.add_argument("--rsi-max", type=float, default=65.0)
    parser.add_argument("--atr-pct-min", type=float, default=0.5)
    parser.add_argument("--atr-pct-max", type=float, default=5.0)
    parser.add_argument("--fib-lookback-bars", type=int, default=120)
    parser.add_argument("--fib-retracement-level", type=float, default=0.786)
    parser.add_argument("--fib-distance-max-pct", type=float, default=2.0)
    parser.add_argument(
        "--fib-anchor-mode",
        choices=["rolling", "swing"],
        default="rolling",
        help="Fibonacci anchor mode: rolling window highs/lows or confirmed swing pivots.",
    )
    parser.add_argument("--fib-swing-left-bars", type=int, default=8)
    parser.add_argument("--fib-swing-right-bars", type=int, default=8)
    parser.add_argument("--claim-min-coverage", type=float, default=0.2)
    parser.add_argument("--claim-min-winrate-delta-pp", type=float, default=5.0)
    parser.add_argument("--volume-window-bars", type=int, default=20)
    parser.add_argument("--gatekeeper-score-window", type=int, default=180)
    parser.add_argument("--gatekeeper-score-quantile", type=float, default=0.70)
    parser.add_argument("--gatekeeper-fallback-threshold", type=float, default=0.55)
    parser.add_argument("--gatekeeper-min-volume-rel", type=float, default=0.8)
    parser.add_argument("--gatekeeper-adx-floor", type=float, default=20.0)

    args = parser.parse_args()

    start_date = parse_utc(args.start_date)
    end_date = parse_utc(args.end_date)
    if end_date <= start_date:
        raise ValueError("end-date must be after start-date")

    thresholds = [float(x.strip()) for x in args.thresholds.split(",") if x.strip()]

    client = ApiClient(
        base_url=args.base_url,
        timeout_seconds=args.timeout_seconds,
        retries=args.retries,
        retry_sleep_seconds=args.retry_sleep_seconds,
    )

    if args.backtest_json:
        backtest = json.loads(Path(args.backtest_json).read_text(encoding="utf-8"))
    else:
        backtest = client.get_json(
            "/Backtest",
            {
                "startDate": to_iso_utc(start_date),
                "endDate": to_iso_utc(end_date),
            },
        )

    closed_deals = extract_closed_deals(backtest)
    if not args.include_hard_stop_loss:
        closed_deals = [d for d in closed_deals if d.sell_advice.lower() != "hardstoploss"]

    if not closed_deals:
        raise RuntimeError("No closed deals found for selected period")

    adx_cache: dict[str, float] = {}
    enriched: list[EnrichedDeal] = []
    missing_adx: list[dict[str, str]] = []

    candle_times_ms: list[int] = []
    candle_adx: list[float | None] = []
    candle_rsi: list[float | None] = []
    candle_ema: list[float | None] = []
    candle_atr_pct: list[float | None] = []
    candle_fib_retr: list[float | None] = []
    candle_volume_rel: list[float | None] = []
    candle_volume_z: list[float | None] = []
    closes: list[float] = []

    if args.adx_source == "binance-klines":
        klines = fetch_binance_klines(
            interval=args.binance_interval,
            start=start_date,
            end=end_date,
            timeout_seconds=args.timeout_seconds,
            retries=args.retries,
            retry_sleep_seconds=args.retry_sleep_seconds,
        )
        if not klines:
            raise RuntimeError("No Binance klines returned for selected period")

        highs = [float(row[2]) for row in klines]
        lows = [float(row[3]) for row in klines]
        closes = [float(row[4]) for row in klines]
        volumes = [float(row[5]) for row in klines]
        open_times = [int(row[0]) for row in klines]
        interval_ms = interval_to_ms(args.binance_interval)
        candle_times_ms = [ot + interval_ms - 1 for ot in open_times]
        candle_adx = compute_adx_series(highs, lows, closes, args.adx_period)
        candle_rsi = compute_rsi_series(closes, args.rsi_period)
        candle_ema = compute_ema_series(closes, args.ema_period)
        candle_atr = compute_atr_series(highs, lows, closes, args.atr_period)
        candle_atr_pct = [
            ((a / c) * 100.0) if (a is not None and c > 0) else None
            for a, c in zip(candle_atr, closes)
        ]
        if args.fib_anchor_mode == "rolling":
            candle_fib_retr = compute_fib_retracement_series(
                highs,
                lows,
                lookback_bars=args.fib_lookback_bars,
                retracement_level=args.fib_retracement_level,
            )
        else:
            candle_fib_retr = compute_fib_retracement_series_swing(
                highs,
                lows,
                left_bars=args.fib_swing_left_bars,
                right_bars=args.fib_swing_right_bars,
                retracement_level=args.fib_retracement_level,
            )

        for i in range(len(volumes)):
            start = max(0, i - args.volume_window_bars + 1)
            window = volumes[start : i + 1]
            if not window:
                candle_volume_rel.append(None)
                candle_volume_z.append(None)
                continue
            mean_vol = sum(window) / len(window)
            var = sum((v - mean_vol) ** 2 for v in window) / len(window)
            std = math.sqrt(var)
            rel = volumes[i] / mean_vol if mean_vol > 0 else math.nan
            z = (volumes[i] - mean_vol) / std if std > 1e-12 else 0.0
            candle_volume_rel.append(rel)
            candle_volume_z.append(z)

    for deal in closed_deals:
        buy_iso = to_iso_utc(deal.buy_timestamp)
        if buy_iso not in adx_cache:
            try:
                if args.adx_source == "indicatorresults":
                    payload = client.get_json(
                        "/indicatorresults",
                        {
                            "startHistory": buy_iso,
                            "timeSpan": args.time_span,
                            "indicator": "adx",
                            "period": args.adx_period,
                        },
                    )
                    adx_cache[buy_iso] = extract_adx_value(payload)
                else:
                    buy_ms = int(deal.buy_timestamp.timestamp() * 1000)
                    idx = bisect.bisect_right(candle_times_ms, buy_ms) - 1
                    if idx < 0:
                        raise ValueError("No candle before buy timestamp")
                    adx_val = candle_adx[idx]
                    if adx_val is None:
                        raise ValueError("ADX warmup not available at buy timestamp")
                    adx_cache[buy_iso] = float(adx_val)
            except Exception as exc:  # noqa: BLE001
                missing_adx.append(
                    {
                        "buy_timestamp": buy_iso,
                        "error": str(exc),
                    }
                )
                continue

        adx_value = adx_cache[buy_iso]
        rsi_value = math.nan
        ema_value = math.nan
        close_value = deal.buy_price
        atr_pct_value = math.nan
        ema_slope_value = math.nan
        fib_retr_value = math.nan
        fib_distance_value = math.nan
        volume_rel_value = math.nan
        volume_z_value = math.nan

        if args.adx_source == "binance-klines":
            buy_ms = int(deal.buy_timestamp.timestamp() * 1000)
            idx = bisect.bisect_right(candle_times_ms, buy_ms) - 1
            if idx >= 0:
                close_value = closes[idx]
                if idx < len(candle_rsi) and candle_rsi[idx] is not None:
                    rsi_value = float(candle_rsi[idx])
                if idx < len(candle_ema) and candle_ema[idx] is not None:
                    ema_value = float(candle_ema[idx])
                if idx < len(candle_atr_pct) and candle_atr_pct[idx] is not None:
                    atr_pct_value = float(candle_atr_pct[idx])
                if idx < len(candle_fib_retr) and candle_fib_retr[idx] is not None:
                    fib_retr_value = float(candle_fib_retr[idx])
                    if close_value > 0:
                        fib_distance_value = abs(close_value - fib_retr_value) / close_value * 100.0
                if idx < len(candle_volume_rel) and candle_volume_rel[idx] is not None:
                    volume_rel_value = float(candle_volume_rel[idx])
                if idx < len(candle_volume_z) and candle_volume_z[idx] is not None:
                    volume_z_value = float(candle_volume_z[idx])
                slope_idx = idx - args.ema_slope_bars
                if (
                    slope_idx >= 0
                    and idx < len(candle_ema)
                    and slope_idx < len(candle_ema)
                    and candle_ema[idx] is not None
                    and candle_ema[slope_idx] is not None
                ):
                    ema_slope_value = float(candle_ema[idx] - candle_ema[slope_idx])

        enriched.append(
            EnrichedDeal(
                buy_timestamp=deal.buy_timestamp,
                sell_timestamp=deal.sell_timestamp,
                buy_advice=deal.buy_advice,
                sell_advice=deal.sell_advice,
                buy_price=deal.buy_price,
                sell_price=deal.sell_price,
                profit_percent=deal.profit_percent,
                is_loss=deal.profit_percent < 0.0,
                adx=adx_value,
                rsi=rsi_value,
                ema200=ema_value,
                close_at_buy=close_value,
                atr_percent=atr_pct_value,
                ema200_slope_bars=ema_slope_value,
                fib_retracement_price=fib_retr_value,
                fib_distance_percent=fib_distance_value,
                volume_rel_20=volume_rel_value,
                volume_zscore_20=volume_z_value,
                gatekeeper_score_v2=math.nan,
                gatekeeper_threshold_v2=math.nan,
                gatekeeper_pass_v2=False,
            )
        )

    if not enriched:
        raise RuntimeError("Unable to resolve ADX for any closed deal in selected period")

    missing_share = len(missing_adx) / len(closed_deals)
    if missing_share > args.max_missing_adx_share:
        raise RuntimeError(
            f"Too many deals missing ADX values: {len(missing_adx)}/{len(closed_deals)} "
            f"({missing_share:.1%}) exceeds max {args.max_missing_adx_share:.1%}"
        )

    losses = [d.adx for d in enriched if d.is_loss]
    wins = [d.adx for d in enriched if not d.is_loss]

    baseline_winrate = 100.0 * len(wins) / len(enriched)
    baseline_lossrate = 100.0 - baseline_winrate

    mw = mann_whitney_u_test(wins, losses)
    auc = mw["u"] / (len(wins) * len(losses)) if wins and losses else math.nan

    threshold_rows = summarize_thresholds(enriched, thresholds)

    if args.adx_source == "binance-klines":
        build_gatekeeper_v2_scores(
            enriched,
            score_window=args.gatekeeper_score_window,
            score_quantile=args.gatekeeper_score_quantile,
            fallback_threshold=args.gatekeeper_fallback_threshold,
            adx_floor=args.gatekeeper_adx_floor,
            fib_max_dist_pct=args.fib_distance_max_pct,
            mandatory_min_volume_rel=args.gatekeeper_min_volume_rel,
        )

    gatekeeper_rows: list[dict[str, float]] = []
    if args.adx_source == "binance-klines":
        gates = {
            "adx_gte_30": lambda d: d.adx >= 30.0,
            "trend_close_above_ema200": lambda d: (not math.isnan(d.ema200)) and d.close_at_buy > d.ema200,
            "trend_ema200_slope_up": lambda d: (not math.isnan(d.ema200_slope_bars)) and d.ema200_slope_bars > 0.0,
            "rsi_in_band": lambda d: (not math.isnan(d.rsi)) and args.rsi_min <= d.rsi <= args.rsi_max,
            "atr_pct_in_band": lambda d: (not math.isnan(d.atr_percent)) and args.atr_pct_min <= d.atr_percent <= args.atr_pct_max,
            "fib_near_retracement": lambda d: (not math.isnan(d.fib_distance_percent)) and d.fib_distance_percent <= args.fib_distance_max_pct,
            "combo_trend_adx": lambda d: d.adx >= 30.0 and (not math.isnan(d.ema200)) and d.close_at_buy > d.ema200,
            "combo_trend_adx_rsi": lambda d: (
                d.adx >= 30.0
                and (not math.isnan(d.ema200))
                and d.close_at_buy > d.ema200
                and (not math.isnan(d.rsi))
                and args.rsi_min <= d.rsi <= args.rsi_max
            ),
            "combo_trend_adx_rsi_atr": lambda d: (
                d.adx >= 30.0
                and (not math.isnan(d.ema200))
                and d.close_at_buy > d.ema200
                and (not math.isnan(d.rsi))
                and args.rsi_min <= d.rsi <= args.rsi_max
                and (not math.isnan(d.atr_percent))
                and args.atr_pct_min <= d.atr_percent <= args.atr_pct_max
            ),
            "combo_trend_adx_fib": lambda d: (
                d.adx >= 30.0
                and (not math.isnan(d.ema200))
                and d.close_at_buy > d.ema200
                and (not math.isnan(d.fib_distance_percent))
                and d.fib_distance_percent <= args.fib_distance_max_pct
            ),
            "combo_trend_adx_rsi_fib": lambda d: (
                d.adx >= 30.0
                and (not math.isnan(d.ema200))
                and d.close_at_buy > d.ema200
                and (not math.isnan(d.rsi))
                and args.rsi_min <= d.rsi <= args.rsi_max
                and (not math.isnan(d.fib_distance_percent))
                and d.fib_distance_percent <= args.fib_distance_max_pct
            ),
            "gk_v2_mandatory_only": lambda d: (
                (not math.isnan(d.ema200))
                and d.close_at_buy > d.ema200
                and (not math.isnan(d.ema200_slope_bars))
                and d.ema200_slope_bars > 0.0
                and (not math.isnan(d.volume_rel_20))
                and d.volume_rel_20 >= args.gatekeeper_min_volume_rel
            ),
            "gk_v2_score_dynamic": lambda d: d.gatekeeper_pass_v2,
            "gk_v2_trend_adx_volume": lambda d: (
                d.gatekeeper_pass_v2
                and d.adx >= 30.0
                and (not math.isnan(d.volume_zscore_20))
                and d.volume_zscore_20 >= 0.0
            ),
            "gk_v2_trend_adx_fib": lambda d: (
                d.gatekeeper_pass_v2
                and d.adx >= 30.0
                and (not math.isnan(d.fib_distance_percent))
                and d.fib_distance_percent <= args.fib_distance_max_pct
            ),
            "gk_v2_trend_adx_fib_rsi": lambda d: (
                d.gatekeeper_pass_v2
                and d.adx >= 30.0
                and (not math.isnan(d.fib_distance_percent))
                and d.fib_distance_percent <= args.fib_distance_max_pct
                and (not math.isnan(d.rsi))
                and args.rsi_min <= d.rsi <= args.rsi_max
            ),
        }
        gatekeeper_rows = summarize_named_gates(
            enriched,
            gates,
            min_coverage_for_claim=args.claim_min_coverage,
            min_effect_pp_for_claim=args.claim_min_winrate_delta_pp,
        )

    meets_significance = bool(not math.isnan(mw["p_value"]) and mw["p_value"] < 0.05)
    best_threshold_row = None
    eligible = [row for row in threshold_rows if row["count"] > 0 and row["coverage"] >= 0.2]
    if eligible:
        best_threshold_row = max(eligible, key=lambda row: row["winrate_delta_pp"])

    meets_effect = bool(
        best_threshold_row
        and not math.isnan(best_threshold_row["winrate_delta_pp"])
        and best_threshold_row["winrate_delta_pp"] >= 5.0
    )

    best_gatekeeper_row = None
    valid_gatekeeper_claims = [row for row in gatekeeper_rows if row.get("gatekeeper_claim_valid")]
    if valid_gatekeeper_claims:
        best_gatekeeper_row = max(valid_gatekeeper_claims, key=lambda row: row["winrate_delta_pp"])
    elif gatekeeper_rows:
        best_gatekeeper_row = gatekeeper_rows[0]

    summary = {
        "meta": {
            "generated_at_utc": to_iso_utc(datetime.now(tz=timezone.utc)),
            "base_url": args.base_url,
            "backtest_json": args.backtest_json,
            "adx_source": args.adx_source,
            "start_date": to_iso_utc(start_date),
            "end_date": to_iso_utc(end_date),
            "time_span": args.time_span,
            "binance_interval": args.binance_interval,
            "adx_period": args.adx_period,
            "include_hard_stop_loss": args.include_hard_stop_loss,
            "rsi_period": args.rsi_period,
            "ema_period": args.ema_period,
            "atr_period": args.atr_period,
            "ema_slope_bars": args.ema_slope_bars,
            "rsi_band": [args.rsi_min, args.rsi_max],
            "atr_pct_band": [args.atr_pct_min, args.atr_pct_max],
            "fib_lookback_bars": args.fib_lookback_bars,
            "fib_retracement_level": args.fib_retracement_level,
            "fib_distance_max_pct": args.fib_distance_max_pct,
            "fib_anchor_mode": args.fib_anchor_mode,
            "fib_swing_left_bars": args.fib_swing_left_bars,
            "fib_swing_right_bars": args.fib_swing_right_bars,
            "claim_min_coverage": args.claim_min_coverage,
            "claim_min_winrate_delta_pp": args.claim_min_winrate_delta_pp,
            "volume_window_bars": args.volume_window_bars,
            "gatekeeper_score_window": args.gatekeeper_score_window,
            "gatekeeper_score_quantile": args.gatekeeper_score_quantile,
            "gatekeeper_fallback_threshold": args.gatekeeper_fallback_threshold,
            "gatekeeper_min_volume_rel": args.gatekeeper_min_volume_rel,
            "gatekeeper_adx_floor": args.gatekeeper_adx_floor,
        },
        "sample": {
            "closed_deals_total": len(closed_deals),
            "closed_deals_with_adx": len(enriched),
            "closed_deals_missing_adx": len(missing_adx),
            "closed_deals_missing_adx_share": missing_share,
            "wins": len(wins),
            "losses": len(losses),
            "baseline_winrate_percent": baseline_winrate,
            "baseline_lossrate_percent": baseline_lossrate,
            "median_adx_wins": median(wins),
            "median_adx_losses": median(losses),
        },
        "statistics": {
            "mann_whitney_u": mw,
            "roc_auc_from_ranks": auc,
        },
        "threshold_sweep": threshold_rows,
        "gatekeeper_comparison": gatekeeper_rows,
        "decision": {
            "meets_significance": meets_significance,
            "meets_effect": meets_effect,
            "best_threshold_min_20pct_coverage": best_threshold_row,
            "thesis_validated": meets_significance and meets_effect,
            "notes": "Validation rule: p < 0.05 and winrate delta >= 5pp at >=20% coverage.",
        },
        "gatekeeper_decision": {
            "best_gate": best_gatekeeper_row,
            "valid_gate_claim_count": len(valid_gatekeeper_claims),
            "any_gatekeeper_valid": len(valid_gatekeeper_claims) > 0,
            "notes": "Gatekeeper valid if p < 0.05 vs baseline winrate, winrate delta >= configured minimum, and coverage >= configured minimum.",
        },
        "missing_adx_examples": missing_adx[:20],
    }

    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    summary_path = output_dir / "adx_loss_validation_summary.json"
    deals_path = output_dir / "adx_loss_validation_deals.csv"

    summary_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    write_csv(deals_path, enriched)

    print(json.dumps(summary, indent=2))
    print(f"\nWrote summary: {summary_path}")
    print(f"Wrote deals CSV: {deals_path}")


if __name__ == "__main__":
    main()
