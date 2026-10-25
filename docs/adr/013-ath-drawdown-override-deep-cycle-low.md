# ADR-013 — ATH-Drawdown Override: Deep-Cycle-Low Einstiegsfenster

## Status
Accepted – 2026-05-08

## Kontext

Der BearMarketFilter (ADR-011) blockiert alle Kaufsignale, solange der BTC-Preis unter seiner 200-Tage-SMA liegt.
Diese Regel verhindert zuverlässig Käufe ins fallende Messer während nachhaltiger Bärenmärkte.

**Problem:** Jeder Bitcoin-Zyklus endet in einer extremen Übertreibungsphase nach unten (Deep-Cycle-Low):

| Zyklus | Peak    | Tief         | Drawdown | Recovery  |
|--------|---------|--------------|----------|-----------|
| 2011   | ~$30    | ~$2          | -93 %    | +9 300 %  |
| 2013   | ~$1.1 k | ~$150        | -87 %    | +7 000 %  |
| 2017   | ~$20 k  | ~$3 150      | -84 %    | +2 300 %  |
| 2021   | ~$69 k  | ~$15 500     | -77 %    | +247 %    |
| 2026   | ~$125 k | ~$37 500 (Prognose) | -70 % (Prognose) | +500 % (Prognose) |
| 2029   | ~$189 k | ~$82 500 (Prognose)   | -67 % (Prognose) | ?         |


An diesen Tiefpunkten ist der Kurs auch weit unter der 200d-SMA → der Filter blockiert genau die besten Einstiegschancen.

**Diagnose (Backtest Nov 2018 – Jul 2019):**
```
rawBuySignals:             242
postRegimeBuySignals:       49
blockedBuyDueToBearMarket:  49  ← ALLE davon geblockt
executedBuys:                0
```

**Risikoabwägung:** Bei einem bereits eingetretenen 75 %+ Drawdown vom ATH:
- Weiterer Rückgang von -10 bis -20 % möglich, aber historisch selten mehr
- Upside-Potenzial: mehrere hundert bis tausende Prozent
- Downside ist temporär und kann ausgesessen werden (Long-only-Strategie)

**Beobachtung (Cycle Compression):**
- Der maximale Drawdown wurde historisch tendenziell kleiner (ca. -93 % → -87 % → -84 % → -77 %).
- Die Recovery fiel ebenfalls deutlich geringer aus als in frühen Zyklen.
- Konsequenz: 75 % bleibt als konservativer Trigger aktiv; bei fortschreitender Kompression kann ein adaptiver Trigger (z. B. 70–75 %) sinnvoll werden.

## Entscheidung

**Neues Gate: ATH-Drawdown-Override** (`AthDrawdownOverrideEnabled`)

Wenn der BearMarketFilter aktiv ist **UND** der aktuelle Drawdown vom historischen ATH ≥ 75 % beträgt, wird der Bear-Market-Block **überbrückt**.

Die übrigen Gates bleiben unverändert aktiv:
- Confluence-Schwellen (BullishMinAlignedRanges ≥ 2, BullishMaxTrendCountSum ≤ 2)
- Signal-Follow-Through-Filter
- Re-Entry-Cooldown
- `entryRequiresPriceAboveSma200` (SMA200-Entry-Gate im Backtest-Parameter-Set)

**Konfiguration (`appsettings.json`):**
```json
"BearMarketFilter": {
  "Enabled": true,
  "MovingAveragePeriod": 200,
  "Interval": "1d",
  "AthDrawdownOverrideEnabled": true,
  "AthDrawdownOverrideThresholdPercent": 75.0,
  "AthDrawdownInterval": "1h",
  "AthDrawdownSuppressBearishSellEnabled": true,
  "AthDrawdownSuppressBearishSellThresholdPercent": 75.0,
  "AthDrawdownSuppressInterval": "1d",
  "AthDrawdownAdaptiveThresholdEnabled": true,
  "AthDrawdownAdaptiveMode": "FormulaByCycle",
  "AthDrawdownAdaptiveBasePercent": 100.0,
  "AthDrawdownAdaptiveStepPercent": 7.0,
  "AthDrawdownAdaptiveCycleDrawdownPercents": [93.0, 87.0, 84.0, 77.0, 70.0],
  "AthDrawdownAdaptivePreviousCycleDeltaPercent": 7.0,
  "AthDrawdownAdaptiveCycleStartYears": [2011, 2013, 2017, 2021, 2026]
}
```

**Implementierung:**
- `IBearMarketFilterService.IsDeepCycleLowAsync(interval, asOfUtc, drawdownThresholdPercent, ct)`
  berechnet `(latestLow - athHigh) / athHigh * 100` und gibt `true` zurück, wenn dieser Wert ≤ -75 %.
- `latestLow` = Tief der jüngsten Candle im `AthDrawdownInterval` (aktuell `1h`).
- `athHigh` = Maximum aller Candle-Highs bis `asOfUtc`.
- Ergebnis wird 24h gecacht (analog zu `IsBearMarketAsync`).
- Parity: identische Logik in `BacktestService` (beide Loops) und `TradeOpportunityService`.

## Phase 2 — ATH-Drawdown Sell-Suppression

### Problem

Mit Phase 1 (Buy-Override) allein: Die Position wird Nov 2018 bei $5.662 eröffnet, aber ein bearishes Confluence-Signal schließt sie Feb 2019 bei $3.458 → **-39 %**. Das Ziel — die Erholung aussitzen — wird nicht erreicht.

### Entscheidung

**Neues Gate: `AthDrawdownSuppressBearishSellEnabled`**

Solange das System in einer Deep-Cycle-Low-Zone ist (Drawdown ≥ 75 % vom ATH), werden reguläre bearische Sell-Signale **unterdrückt**.

Ausnahmen (Notfall-Exits):
- `TradeAdvice.Contains("FireSell")` — z. B. FireSell, FireSellStrong
- `TradeAdvice.Contains("HardStop")` — HardStopLoss
- `TradeAdvice.Contains("TrailingStop")` — TrailingStop, AtrStop

Diese drei Bypass-Bedingungen werden mit `.Contains()` geprüft und können nicht unterdrückt werden.

### Backtest-Ergebnis (Nov 2018 – Jan 2020)

| Phase          | Einstieg    | Ausstieg   | Advice         | P/L     |
|----------------|-------------|------------|----------------|---------|
| Phase 1 only   | 22.11.2018 $5.662 | 10.02.2019 $3.458 | bearish Sell | **-39 %** |
| Phase 1 + 2    | 22.11.2018 $5.662 | 29.09.2019 $8.340 | TrailingStop   | **+47 %** |

Der bearische Sell Feb 2019 wird durch die Suppression blockiert. Die Position wird erst durch den TrailingStop bei $8.340 (Sep 2019) geschlossen.

### Rationale

In einer Zone mit bereits eingetretenem ≥75 %-Drawdown vom ATH gilt:
- Das Marktregime ist *strukturell* bärisch (bestätigt durch SMA200), aber das *Tief* ist bereits wahrscheinlich überschritten
- Bearische Confluence-Signale in dieser Zone sind häufig kurze Bounce-and-Fade-Bewegungen, keine neue Abwärtswelle
- Notfall-Exits (FireSell/HardStop/TrailingStop) schützen weiterhin gegen den Extremfall (z. B. Exchange-Kollaps, systemisches Ereignis)

## Phase 4 — Adaptive Drawdown-Schwelle (Cycle Compression)

### Entscheidung

Die Deep-Cycle-Low-Schwelle wird optional adaptiv nach Zyklus berechnet:

$$
threshold_{cycle} = 100 - 7 \cdot cycle
$$

Zusätzlich wird ein zweiter adaptiver Modus unterstützt:

$$
threshold_{cycle_n} = drawdown_{cycle_{n-1}} - delta
$$

mit Standardwert $delta = 7$ Prozentpunkten.

Als robuste Variante gegen Ausreißerzyklen wird zusaetzlich unterstuetzt:

$$
threshold_{cycle_n} = drawdown_{cycle_{n-1}} - median(\Delta drawdown_{historisch})
$$

dabei ist $\Delta drawdown$ die Sequenz der beobachteten Kompressions-Schritte zwischen vergangenen Zyklen.

Mit der aktuellen Zykluszuordnung (Startjahre: 2011, 2013, 2017, 2021, 2026) ergibt das:
- Zyklus 1: 93 %
- Zyklus 2: 86 %
- Zyklus 3: 79 %
- Zyklus 4: 72 %
- Zyklus 5: 65 %

Die adaptive Schwelle wird einheitlich auf beide Gates angewendet:
- Buy-Override (`AthDrawdownOverrideEnabled`)
- Sell-Suppression (`AthDrawdownSuppressBearishSellEnabled`)

Die Notfall-Exits bleiben unverändert ununterdrückbar (FireSell/HardStop/TrailingStop).

### Implementierungsdetails

- Neue Policy: `AthDrawdownThresholdPolicy` (shared für Backtest und Realtime)
- Neuer Modus-Schalter: `AthDrawdownAdaptiveMode`
  - `FormulaByCycle` (bestehende Formel `100 - step * cycle`)
  - `PreviousCycleMinusDelta` (Vorzyklus-Drawdown minus Delta)
  - `PreviousCycleMinusMedianDelta` (Vorzyklus-Drawdown minus robustem Median-Delta)
- Zyklusnummer über konfigurierte `AthDrawdownAdaptiveCycleStartYears`
- Für `PreviousCycleMinusDelta`: `AthDrawdownAdaptiveCycleDrawdownPercents` + `AthDrawdownAdaptivePreviousCycleDeltaPercent`
- Für `PreviousCycleMinusMedianDelta`: Median der historischen Drawdown-Differenzen bis inkl. Vorzyklus (Fallback auf konfiguriertes Delta bei zu wenig Historie)
- Clamp auf gültigen Bereich per `AthDrawdownAdaptiveMinThresholdPercent` / `AthDrawdownAdaptiveMaxThresholdPercent`
- Wenn `AthDrawdownAdaptiveThresholdEnabled = false`, bleibt das Verhalten bei den statischen Schwellwerten

## Konsequenzen

**Positiv:**
- Dec 2018 ($3 150, -84 % vom ATH $20 k) wird als Einstiegssignal qualifiziert
- Jun 2022-Nov 2022 (~$15 500–$17 k, -77 % vom $69 k ATH) kann qualifiziert werden
- Asymmetrie wird ausgenutzt: begrenztes Restrisiko, enormes Upside
- Sell-Suppression verhindert vorzeitigen Ausstieg während der Erholung

**Risiken:**
- Confluence-Signale im Bärenmarkt können trotzdem früh kommen (z. B. erster Bounce bei -78 %, dann nochmal -20 % tiefer) → Deshalb bleibt der Cooldown und Follow-Through aktiv
- Sell-Suppression verlängert Haltezeit auch bei tiefen Buchverlusten — "aussitzen" als Strategie setzt langfristigen Anlagehorizont voraus
- Schwellwert 75 % basiert auf 4 historischen Zyklen; künftige Zyklen könnten flachere Korrekturen zeigen
- Werte können per Config angepasst werden

## Verwandte ADRs
- ADR-011: BearMarketFilter (200d-SMA)
- ADR-006: Confluence-Kalibrierung
- ADR-008: Signal-Follow-Through-Filter
