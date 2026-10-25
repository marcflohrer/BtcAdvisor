# ADR 006 – NearAthSellSuppressMinPercent erhöht von 65% auf 80%

## Status
Accepted – 2026-05-12

## Context

`NearAthSellSuppressEnabled` verhindert Sell-Signale, wenn der BTC-Preis nahe am laufenden ATH liegt. Die Idee: In einer Akkumulationsphase nach einer ATH-Brechung sollen Sell-Signale unterdrückt werden, um vorzeitige Exits zu vermeiden.

### Problem im 2024-Zyklus

Konfiguration vor dieser Änderung: `NearAthSellSuppressMinPercent = 65.0`

Ablauf 2024:
- BTC kauft bei $41.580 (Jan 2024). Vorheriger ATH = $73k (2021).
- 65% von $73k = $47.450 → bereits bei $47k wird die Suppression aktiv.
- BTC bricht im Q1 2024 durch $73k → neues ATH. Jetzt ist 65% vom neuen ATH dauerhaft im Spiel ($108k × 0.65 = $70k), was bei BTC-Kursen von $41k–$87k nie unterschritten wird.
- Resultat: **0 Sells im gesamten Zeitraum 2024–2026-01-01**, unrealisierter Verlust vs. HODL.

## Decision

`NearAthSellSuppressMinPercent` wird von **65%** auf **80%** erhöht.

## Sweep-Ergebnisse

Backtest-Sweep über `nearAthSellSuppressMinPercent` Query-Parameter (neu hinzugefügt):

### 2024-01-01 bis 2026-01-01 (HODL = +108%)

| MinPercent | totalProfitPercent | Buys | Sells |
|---|---|---|---|
| 65% (alt) | — (0 Sells, offene Position) | 1 | 0 |
| 75% | +16.16% | 2 | 1 |
| 80% | +51.65% | 2 | 2 |
| 85% | +51.65% | 2 | 2 |
| 90% | +66.54% | 2 | 2 |
| 101% (aus) | +72.62% | 2 | 2 |

### 2017-01-01 bis 2024-01-01 (HODL = +943%, Baseline = +920%)

| MinPercent | totalProfitPercent | Buys | Sells |
|---|---|---|---|
| 65% (alt) | +920.27% | 3 | 2 |
| 75% | +920.27% | 3 | 2 |
| 80% | +920.27% | 3 | 2 |
| 85% | +920.27% | 3 | 2 |
| 90% | +794.99% ⚠️ | 6 | 5 |
| 101% (aus) | +344.02% ❌ | 6 | 5 |

### Schlussfolgerung

- **75–85%**: 2017-2024 bleibt bei +920% (DistributionSell-Exits bei $14.9k und $60k bleiben erhalten).
- **90%+**: 2017-2024 bricht ein (2021-Run wird durch frühzeitige Sells gestört).
- **80%** ist die minimale Änderung, die 2024-Sells ermöglicht und 2017-2024 nicht beeinträchtigt.

Mit 80%:
- Bei $73k ATH: Suppression ab $58.4k → $41k–$58k-Range kann normal verkaufen
- Bei $108k ATH: Suppression ab $86.4k → erst bei sehr hohen Kursen aktiv

## Consequences

- `appsettings.json`: `NearAthSellSuppressMinPercent` = 80.0
- Neuer Query-Parameter `nearAthSellSuppressMinPercent` am `/Backtest`-Endpoint für zukünftige Sweeps ohne Rebuild.
- Konvention: `0.0` = appsettings, `> 100` = Feature deaktiviert.
- 2024-2026: +51.65% vs. HODL +108% — immer noch hinter HODL, aber die Position wird jetzt korrekt geschlossen. Die Unterperformance liegt am schlechten Buy-Timing, nicht an der Suppression.
- 2017-2024: +920% bleibt unverändert.

## Alternatives Considered

- **90%**: Höheres Upside in 2024 (+66%), aber zerstört 2017-2024 (-125pp). Verworfen.
- **TrailingStop-Anpassung**: Alle TS-Kombos (10/15/20%, Activation 15/25/50%) verringern 2017-2024 massiv (20–106%). Verworfen.
- **Feature ausschalten**: +72% in 2024, aber +344% in 2017-2024 statt +920%. Verworfen.
