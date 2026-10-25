# ADR-017: MacroOnly als Standard-Modus für Backtest

**Datum:** 2026-05-14  
**Status:** Akzeptiert

## Kontext

Das Realtime-System (BitcoinAdviceChangeService, BitcoinController /rerun) ist seit ADR-008 vollständig auf
`MacroOnly = true` (hardcoded in appsettings.json) festgelegt.  
Der Backtest-Pfad (`RunBacktestAsync`, `RunBacktestCoreAsync`) unterstützt `macroOnlyMode` schon als optionalen
Parameter, der jedoch auf `false` stand. Das führte zur Divergenz:

- Realtime: ignoriert den Confluence-Advisor, entscheidet nur via Makro-Gates (DeepCycleLow, CycleTop, PiCycle, …)
- Backtest-Default: rief `IBitcoinTradeAdvisor.GetBitcoinTradeAdvice` auf und nutzte damit den Confluence-Advisor

Das verletzt das Paritätsprinzip: *Replay and realtime trading decisions must use one shared decision policy.*

## Entscheidung

`macroOnlyMode` wird auf `true` als Default gesetzt in:

| Ort | Vorher | Nachher |
|---|---|---|
| `BacktestService.RunBacktestAsync` (voller Overload) | `bool macroOnlyMode = false` | `bool macroOnlyMode = true` |
| `BacktestService.RunBacktestCoreAsync` | `bool macroOnlyMode = false` | `bool macroOnlyMode = true` |
| `BacktestController.RunBacktest` (Query-Parameter) | `[FromQuery] bool macroOnlyMode = false` | `[FromQuery] bool macroOnlyMode = true` |

Auswirkung: `SweepThresholdsAsync` (ruft `RunBacktestCoreAsync` ohne explizite Angabe auf) arbeitet nun ebenfalls
standardmäßig im MacroOnly-Modus.

Der Advisor-Pfad bleibt über `macroOnlyMode=false` weiterhin explizit aufrufbar (für Forschungs-/Vergleichszwecke).

## Beibehaltene Ausnahmen

`GetDecisionDiagnosticsAsync` und `GetBuySignalForwardReturnsAsync` nutzen weiterhin den Advisor — sie sind
explizite Confluence-Analyse-Werkzeuge (AlignedRanges, TrendCountSum, DecisionSource) und treffen keine
Handelsentscheidungen. Das Paritätsprinzip gilt hier nicht.

## Konsequenzen

- Backtest-Ergebnisse (Standard-API-Aufruf ohne Parameter) bilden jetzt das Realtime-Verhalten korrekt ab.
- `IBitcoinTradeAdvisor` bleibt in `BacktestService` registriert, bis die Diagnostik-Methoden ebenfalls umgestellt
  oder entfernt werden.
- Historische Sweep-Ergebnisse, die mit `macroOnlyMode=false` (Advisor) erstellt wurden, sind nicht direkt mit
  neuen Sweep-Ergebnissen vergleichbar.
