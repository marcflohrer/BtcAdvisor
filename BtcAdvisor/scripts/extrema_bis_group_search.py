#!/usr/bin/env python3
"""Analyze missed turning points and extract BIS candidate groups for linear condition design.

Workflow:
1) Load turning-point match audit (price + max day distance).
2) Keep missed extrema only.
3) For each missed extremum, load neighborhood stats and collect strong directional BIS candidates.
4) Group candidates by similarity buckets and emit a ranked summary.

This script does not mutate server state.
"""

from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.parse import urlencode
from urllib.request import urlopen


@dataclass
class Candidate:
    extremum_ts: str
    direction: str
    day_ts: str
    close_price: float
    day_distance: int
    aligned_ranges: int
    aligned_anchor_ranges: int
    trend_sum: int
    linear_score: float


def to_iso_utc(value: str) -> str:
    dt = datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)
    return dt.isoformat().replace("+00:00", "Z")


def get_json(base_url: str, path: str, params: dict[str, Any]) -> dict[str, Any]:
    query = urlencode(params)
    url = f"{base_url.rstrip('/')}{path}?{query}"
    with urlopen(url, timeout=180) as resp:
        payload = resp.read().decode("utf-8")
    return json.loads(payload)


def compute_linear_score(stats: list[dict[str, Any]], direction: str) -> float:
    score = 0.0
    for s in stats:
        buy = float(s.get("oscillatorBuyCount", 0))
        sell = float(s.get("oscillatorSellCount", 0))
        strong = float(s.get("oscillatorStrongTrendCount", 0))
        medium = float(s.get("oscillatorMediumTrendCount", 0))
        if direction == "Buy":
            score += 2.0 * buy - 1.5 * sell + 1.5 * strong + 0.5 * medium
        else:
            score += 2.0 * sell - 1.5 * buy + 1.5 * strong + 0.5 * medium
    return round(score, 3)


def trend_bucket(trend_sum: int) -> str:
    if trend_sum <= 2:
        return "T0_2"
    if trend_sum <= 4:
        return "T3_4"
    if trend_sum <= 6:
        return "T5_6"
    return "T7_PLUS"


def main() -> None:
    parser = argparse.ArgumentParser(description="Find BIS candidate groups for missed turning points.")
    parser.add_argument("--base-url", default="http://localhost:6003")
    parser.add_argument("--start-utc", required=True)
    parser.add_argument("--end-utc", required=True)
    parser.add_argument("--symbol", default="BTC1D")
    parser.add_argument("--price-tolerance-percent", type=float, default=5.0)
    parser.add_argument("--max-match-days", type=int, default=15)
    parser.add_argument("--min-trend-indicators", type=int, default=3)
    parser.add_argument("--out", default="/tmp/extrema_bis_group_search.json")
    args = parser.parse_args()

    audit = get_json(
        args.base_url,
        "/api/insights/turning-points/match-audit",
        {
            "startUtc": args.start_utc,
            "endUtc": args.end_utc,
            "symbol": args.symbol,
            "isLongTimeframe": "true",
            "priceTolerancePercent": args.price_tolerance_percent,
            "maxMatchDays": args.max_match_days,
        },
    )

    missed = [x for x in audit.get("items", []) if not x.get("isMatched", False)]
    candidates: list[Candidate] = []

    for point in missed:
        center = to_iso_utc(point["timestampUtc"])
        neighborhood = get_json(
            args.base_url,
            "/api/insights/turning-points/neighborhood",
            {
                "centerUtc": center,
                "windowDays": args.max_match_days,
                "minTrendIndicators": args.min_trend_indicators,
                "symbol": args.symbol,
                "isLongTimeframe": "true",
            },
        )

        direction = point["expectedAdvice"]
        point_price = float(point["closePrice"])
        entries = neighborhood.get("entries", [])

        for entry in entries:
            day_ts = to_iso_utc(entry["timestampUtc"])
            day_price = float(entry["closePrice"])
            day_distance = int(abs((datetime.fromisoformat(day_ts.replace("Z", "+00:00")).date() -
                                    datetime.fromisoformat(center.replace("Z", "+00:00")).date()).days))
            if day_distance > args.max_match_days:
                continue

            if direction == "Buy":
                if not entry.get("isBullishConditionCandidate", False):
                    continue
                if day_price > point_price * (1.0 + args.price_tolerance_percent / 100.0):
                    continue
                metrics = entry.get("bullish", {})
            else:
                if not entry.get("isBearishConditionCandidate", False):
                    continue
                if day_price < point_price * (1.0 - args.price_tolerance_percent / 100.0):
                    continue
                metrics = entry.get("bearish", {})

            stats = entry.get("statistics", [])
            score = compute_linear_score(stats, direction)
            candidates.append(
                Candidate(
                    extremum_ts=center,
                    direction=direction,
                    day_ts=day_ts,
                    close_price=day_price,
                    day_distance=day_distance,
                    aligned_ranges=int(metrics.get("alignedRanges", 0)),
                    aligned_anchor_ranges=int(metrics.get("alignedAnchorRanges", 0)),
                    trend_sum=int(metrics.get("trendCountSum", 0)),
                    linear_score=score,
                )
            )

    grouped: dict[str, dict[str, Any]] = {}
    for c in candidates:
        key = f"{c.direction}|A{c.aligned_ranges}|AN{c.aligned_anchor_ranges}|{trend_bucket(c.trend_sum)}"
        bucket = grouped.setdefault(
            key,
            {
                "groupKey": key,
                "count": 0,
                "linearScoreMin": c.linear_score,
                "linearScoreMax": c.linear_score,
                "linearScoreAvg": 0.0,
                "medianDayDistanceHint": 0,
            },
        )
        bucket["count"] += 1
        bucket["linearScoreMin"] = min(bucket["linearScoreMin"], c.linear_score)
        bucket["linearScoreMax"] = max(bucket["linearScoreMax"], c.linear_score)
        bucket["linearScoreAvg"] += c.linear_score
        bucket["medianDayDistanceHint"] += c.day_distance

    for key in grouped:
        g = grouped[key]
        if g["count"] > 0:
            g["linearScoreAvg"] = round(g["linearScoreAvg"] / g["count"], 3)
            g["medianDayDistanceHint"] = round(g["medianDayDistanceHint"] / g["count"], 1)

    groups_sorted = sorted(grouped.values(), key=lambda x: (x["count"], x["linearScoreAvg"]), reverse=True)

    output = {
        "params": {
            "startUtc": args.start_utc,
            "endUtc": args.end_utc,
            "symbol": args.symbol,
            "priceTolerancePercent": args.price_tolerance_percent,
            "maxMatchDays": args.max_match_days,
            "minTrendIndicators": args.min_trend_indicators,
        },
        "auditSummary": {
            "totalTurningPoints": audit.get("totalTurningPoints", 0),
            "matchedCount": audit.get("matchedCount", 0),
            "missedCount": audit.get("missedCount", 0),
        },
        "candidateCount": len(candidates),
        "groups": groups_sorted,
        "topCandidates": [c.__dict__ for c in sorted(candidates, key=lambda x: x.linear_score, reverse=True)[:50]],
        "missedExtrema": missed,
    }

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(output, indent=2), encoding="utf-8")

    print(f"Wrote analysis to {out_path}")
    print(f"Missed extrema: {len(missed)}")
    print(f"Directional BIS candidates: {len(candidates)}")
    print(f"Similarity groups: {len(groups_sorted)}")


if __name__ == "__main__":
    main()
