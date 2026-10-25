# ADR-018: Live-Stops deaktiviert zur Backtest-Paritaet

**Datum:** 2026-05-24
**Status:** Akzeptiert

## Kontext

Audit der Live/Backtest-Paritaet (Mai 2026) ergab eine undokumentierte Divergenz:

- `BacktestService.RunBacktestCoreAsync` (Z. 1591/1602/1613) ueberspringt
  `HardStopLoss`, `AtrStop` und `TrailingStop` wenn `macroOnlyMode == true`.
  Nur `BreakevenStop` bleibt aktiv (eigene `breakevenActive`-Flag).
- `BitcoinAdviceChangeService.SendChangeAlertsToBot` (Live) wertet alle Stops
  immer aus, allein gesteuert durch die `Enable*`-Flags in
  `appsettings.json`.

Prod-Konfiguration vor dieser ADR:

```
RiskControls.EnableHardStopLoss = true
RiskControls.HardStopLossPercent = -10.0
RiskControls.EnableAtrStopLoss   = false   (default)
RiskControls.EnableTrailingStop  = false   (default)
RiskControls.BreakevenTriggerPercent = 0   (default)
```

Damit fuhr live ein -10 % HardStop pro Position, der Backtest aber nicht.

### Quantifizierte Auswirkung auf die beiden bisherigen Macro-Trades

| Trade | Backtest-Profit | MaxAdverseExcursion | Live HardStop -10 % triggert? |
|---|---|---|---|
| 2018-11-24 → 2021-10-22 | +1292 % | -29.76 % | Ja |
| 2022-06-18 → 2025-05-24 | +427 %  | -24.10 % | Ja |

Beide Backtest-Gewinn-Trades wurden live durch den HardStop in eine
~ -10 % Verlust-Position konvertiert. Die Phase-A-Bootstrap-Validierung
(p = 0.004, +6168 % Alpha) ist damit nicht uebertragbar — sie misst
einen Code-Pfad, den die Live-Maschine nicht ausfuehrt.

ADR-017 fordert *"Replay and realtime trading decisions must use one
shared decision policy"*, dokumentiert aber nur den Advisor-Pfad. Die
Stop-Loss-Asymmetrie war nicht erfasst.

## Entscheidung

Die Live-Stops werden deaktiviert, um Live-Verhalten mit dem
Backtest-Standardmodus (`macroOnlyMode = true`) gleichzuziehen.

| Flag | Vorher | Nachher |
|---|---|---|
| `RiskControls.EnableHardStopLoss` | `true` | `false` |
| `RiskControls.EnableAtrStopLoss` | `false` | `false` (unveraendert) |
| `RiskControls.EnableTrailingStop` | `false` | `false` (unveraendert) |
| `RiskControls.BreakevenTriggerPercent` | `0` | `0` (unveraendert) |

Datei: `BtcAdvisor/appsettings.json`.

`appsettings.Development.json` bleibt unveraendert (Dev-Profil ist nicht
geldwirksam und dient der Stop-Logik-Erprobung).

Der `!macroOnlyMode`-Guard im `BacktestService` bleibt vorerst stehen.
Er ist im Prod-Modus (`macroOnlyMode = true`) effektiv neutral und nur
fuer Forschungs-/Vergleichs-Runs (`macroOnlyMode = false`) relevant.

## Konsequenzen

- Live verhaelt sich jetzt genau wie der Backtest unter
  `macroOnlyMode = true`: Exits ausschliesslich ueber Macro-Sells
  (`DistributionSell`, `PiCycleTopSell`, `CycleTopZone`).
- Die im Backtest gemessenen Drawdowns (z. B. MAE -30 % im
  2018-Trade) treten in der Live-Position ungebremst auf.
- Solange die Macro-Sell-Logik versagt (z. B. ein Cycle-Top wird
  verpasst), gibt es keinen Floor mehr. Loss-Exposure ist nach oben
  offen.
- Der Phase-A-Bootstrap-Befund (Alpha +6168 %, p = 0.004 auf
  2018-2026) bleibt anwendbar, da das gemessene System nun auch das
  ausgefuehrte System ist.

## Folge-Aufgaben

- Beobachtung: erste echte Bear-Phase nach Deployment dokumentieren
  (Drawdown-Pfad, ob/wann Macro-Sell greift).
- Aufraeumen: `!macroOnlyMode`-Guard in `BacktestService` kann
  entweder entfernt werden (Stops nie aktiv bei macroOnly) oder das
  ganze Stop-Eval-Block bei `macroOnlyMode` ganz weggelassen werden.
  Aktuell behalten fuer Vergleichsruns.
- Wenn spaeter ein Loss-Floor gewuenscht ist, gemeinsam in Live und
  Backtest gleichzeitig einfuehren (kein `!macroOnlyMode`-Guard
  mehr).
