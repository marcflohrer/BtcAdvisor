# ADR-007: Kalibrierte LTF-Confluence-Schwellen (with-1w Profil)

## Status

Accepted – 2. Mai 2026
Amended – 3. Mai 2026

## Kontext

Die Fehlerquoten des LTF-Confluence-Evaluators waren mit den Standardschwellen (≥3 aligned, ≥1 anchor, 0 conflicts, per-range trend gate) zu hoch:

- Bullish T1: ~86.7% (2025), Bearish T1: ~66.7% (2025)
- Ziel: worst(T1) ≤ 70%, worst(T2) ≤ 25% über alle Jahreszeiträume

## Entscheidung

### 1. `IsAligned`-Definition vereinheitlicht

Der per-range Trend-Gate (`strong+medium > 0`) wurde aus `IsAligned` entfernt.  
`IsAligned` prüft jetzt nur noch `buy > sell` (bullish) bzw. `sell > buy` (bearish).

**Begründung:** InsightsController kalibriert Schwellen gegen die einfachere Definition. Die Kalibrierung wäre ohne diesen Schritt nicht übertragbar.

### 2. Neue Schwellen (LongTimeframeThresholds.cs)

Ermittelt per InsightsController Error-Rate-Sweep:  
`with-1w`, `priceTolerancePercent=10`, `extremaWindowDays=14`, `maxConflictingAnchorRanges=0`  
Fenster: 2024-01-01 bis 2025-12-31, `maxTypeIIErrorPercent=25`, `minSignalDaysPerYear=3`

| Richtung | minAlignedRanges | minAlignedAnchorRanges | maxConflictingAnchorRanges | minTrendCountSum |
|---|---|---|---|---|
| Bullish | **4** (war 3) | **2** (war 1) | 0 | **2** (war 0) |
| Bearish | 3 | **0** (war 1) | 0 | **2** (neu) |

### 2.1 Amendment 2026-05-03: Bullish Trend-Floor angehoben

Zusatzentscheidung: `BullishMinTrendCountSum` wurde von `0` auf `2` erhöht.

Begründung: In Seitwärtsphasen konnte das Buy-Signal trotz `buy > sell` auf mehreren Ranges zu häufig feuern, obwohl kaum Trendstärke vorlag. Der Floor `>= 2` reduziert diese Noise-Buys, ohne die Anchor-Logik zu verändern.

### 3. Erwartete Fehlerquoten nach Änderung

| Fenster | Bull T1 | Bear T1 | Bull T2 | Bear T2 | Status |
|---|---|---|---|---|---|
| 2024 | 66.7% | 66.0% | 0.0% | 25.0% | ✓ ACCEPT |
| 2025 | 41.7% | 52.6% | 0.0% | 0.0% | ✓ ACCEPT |
| 2024+2025 | 66.7% | 68.3% | 20.0% | 20.0% | ✓ ACCEPT |
| 2026 YTD | 33.3% | 0.0% | 0.0% | 0.0% | ✓ ACCEPT |

## Konsequenzen

- `LongTimeframeMultiTrendBullishConditionMet`: Basis-Gate bleibt bei 4 aligned / 2 anchors; zusätzlich gilt jetzt global `minTrendCountSum >= 2` für jedes bullish Confluence-Signal
- `LongTimeframeBearishConfluenceConditionMet`: Anchor-Anforderung entfällt, dafür globaler Trend-Check (≥2) → robusteres Bärensignal ohne Abhängigkeit von 1d/1w Anchor-Alignment
- `LongTimeframeLinearBullish/BearishConditionMet` unverändert; greifen weiterhin als Fallback
- Replay und Realtime verwenden dieselbe `LongTimeframeConfluenceEvaluator`-Logik — Parität ist gewahrt

## Verworfene Alternativen

- **Schwellen gegen alte `IsAligned`-Definition kalibrieren**: würde zusätzliche Iteration erfordern und bringt geringfügig schlechtere Ergebnisse, da der per-range Trend-Gate gültige Bottoms filtert
- **LongTimeframeMultiTrend* entfernen**: Nicht nötig; sie bleiben als Labeling-Schicht für stärkere Signale erhalten
