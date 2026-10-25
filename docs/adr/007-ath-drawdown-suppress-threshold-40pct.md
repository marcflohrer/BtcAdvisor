# ADR-007: ATH-Drawdown Sell-Suppress Threshold auf 40 % gesenkt

## Status

Accepted — 2026-05-10

## Context

Die `AthDrawdownSuppressBearishSellThresholdPercent`-Schwelle bestimmt, ab welchem ATH-Drawdown bearishe Sell-Signale (`LongTimeframeBearishConfluence`, `LongTimeframeMultiTrendBearish`, `LongTimeframeLinearBearish`) unterdrückt werden, solange eine Position offen ist. Der Gedanke: In tiefen Zyklustiefs ist der Restrisiko gering, das Erholungspotenzial asymmetrisch hoch — verfrühte Exits realisieren große Verluste und verpassen den Aufschwung.

Der Standardwert war **75 %** (identisch mit dem Buy-Override-Threshold). Backtest-Auswertung 2020–2022 ergab:

- Strategie: **-5,2 %** vs. HODL **+587 %**
- Ursache: Sell am 11. Oktober 2020 bei $10.667
  - ATH-Drawdown 2017er-Peak ($20 k): **46,7 %** — unter der 75 %-Schwelle → Suppression griff NICHT
  - BTC rallte danach auf $64 k (April 2021 Pi Cycle Top) — die gesamte Aufwärtsbewegung wurde verpasst
  - Nächster Kauf erst Mai 2021 bei $58.241 — kurz vor dem Crash

## Decision

**`AthDrawdownSuppressBearishSellThresholdPercent` von 75 % auf 40 % gesenkt.**

Zusätzlich wurde `AthDrawdownSuppressAdaptiveThresholdEnabled` eingeführt (default `false`). Damit ist der Suppress-Threshold **statisch entkoppelt** vom adaptiven Zyklus-Formel-Threshold (`AthDrawdownAdaptiveThresholdEnabled`), der primär für Buy-Overrides calibriert ist (Werte: 77–93 % je Zyklus). Das statische 40 % gilt zyklusübergreifend für den Sell-Suppress-Pfad.

### Kalibrierung der 40 %-Schwelle

| Datum | Ereignis | Drawdown vom ATH | Suppress greifen? |
|---|---|---|---|
| Okt 2020 | Verfrühter bearisher Sell | 46,7 % (ATH=$20k) | ✅ Ja (46,7 % ≥ 40 %) |
| Aug 2021 | Confluence-Sell bei Erholung | 42,2 % (ATH=$64k) | ✅ Ja (42,2 % ≥ 40 %) |
| Jan 2022 | Echter Bärenstart | 32 % (ATH=$69k) | ❌ Nein (<40 %) → Sell feuert korrekt |

### Backtest-Ergebnis nach Fix (2020–2026)

| Zeitraum | HODL | Strategy vor Fix | Strategy nach Fix |
|---|---|---|---|
| 2020–2022 | +587 % | -5 % | **+473 %** (0.80x HODL) |
| 2020–2026 | +937 % | ~-28 % | **+416 %** (0.44x HODL) |

HODL wird über den Gesamtzeitraum 2020–2026 noch nicht geschlagen (0.44x). Das liegt an Whipsaw-Trades 2024–2025 auf hohem Kursniveau — ein separates Problem (CycleTopSellZone / Buy-Blocking, ADR-008 geplant).

## Consequences

- **Positiv**: Oktober-2020-Fehlausstieg wird korrekt supprimiert; 2020-2022-Periode von -5 % auf +473 % verbessert.
- **Positiv**: Zyklusübergreifend robuste statische Schwelle; nicht von der Buy-Zyklus-Formel abhängig.
- **Risiko**: 40 % ist aggressiver als 75 % — in einem echten Bärenmarkt mit 40 % Drawdown wird nun nicht mehr verkauft (z. B. nach einem 40 %-Rücksetzer von einem höheren Niveau). Notfallexits (FireSell, HardStopLoss, TrailingStop) bleiben immer aktiv.
- **Offen**: CycleTopSellZone muss vor Aktivierung einen Buy-Blocking-Gate erhalten (sonst Tight-Loop: Kauf → sofortiger Zone-Sell → nächster Kauf → ...). Separate Analyse erforderlich.

## Alternatives Considered

- **75 % beibehalten**: Backtest zeigt klar, dass die Schwelle zu konservativ ist und echte Zyklustiefs nicht erkennt.
- **Adaptive Formel auch für Suppress**: Cycle-3-Formel ergibt 79 % für 2017–2021 — gleiches Problem wie 75 %. Die Formel ist auf Buy-Overrides kalibriert, nicht auf Sell-Suppression.
- **MarketRegimeFilter erweitern** (Neutral → auch Suppression): Technisch möglich, aber würde weitere Zustände in den Realtime-Pfad einführen; Suppress-Threshold-Fix ist einfacher und ausreichend.
