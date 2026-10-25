using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;
using System.Globalization;

namespace BtcAdvisor.Controllers;

[ApiController]
[Route("[controller]")]
public class BacktestController(
    IBacktestService backtestService,
    IMonteCarloSignificanceService monteCarloService,
    IBinanceKlineService binanceKlineService,
    IRegimeStabilityService regimeStabilityService,
    IStationaryBootstrapService stationaryBootstrapService) : ControllerBase, IBacktestController
{
    [HttpGet]
    [SwaggerOperation(
        Summary = "Backtesting der aktuellen Strategie.",
        Description = @"
Simuliert die Handelsstrategie über einen historischen Zeitraum und gibt Performance-Metriken zurück.

Die Simulation pflegt den Zustand (letzter Kaufkurs, letzter Verkaufskurs, niedrigster Kauf nach FireSell, etc.)
vollständig **in Memory**.

Mit `persist=true` werden alle erkannten Signale zusätzlich als historische Einträge in
`BuyActionLogs`/`SellActionLogs` persistiert (**Signal-Backfill**).
Bereits vorhandene Einträge werden dank Duplikat-Prüfung übersprungen – der Aufruf ist idempotent.

**Rückgabewerte (BacktestSummary):**
- `firstPrice` / `lastPrice` – Eröffnungs- und Schlusskurs im Zeitraum
- `totalBuys` / `totalSells` – Anzahl der ausgelösten Signale
- `totalProfitPercent` – Summe aller Gewinne/Verluste abgeschlossener Kauf-Verkauf-Paare
- `winRatePercent` – Anteil profitabler Verkäufe
- `maxLossPercent` / `maxGainPercent` – Schlechtester / bester einzelner Trade
- `buyAndHoldPercent` – Vergleichsrendite einer passiven Buy-and-Hold-Strategie
- `trades` – Detaillierte Liste aller ausgelösten Signale mit Zeitstempel, Kurs und Gewinn/Verlust

**Hinweise:**
- Die Datumsangaben müssen im **ISO 8601-Format** vorliegen (z. B. `2025-01-01T00:00:00Z`).
- `endDate` muss **nach** `startDate` liegen.
- Für sehr lange Zeiträume kann die Berechnung mehrere Minuten dauern.
- `persist=true` erfordert, dass Indikatordaten für den Zeitraum vollständig vorhanden sind (Indikator-Backfill zuerst ausführen).
"
    )]
    [SwaggerResponse(200, "Backtest-Ergebnis.", typeof(BacktestSummary))]
    [SwaggerResponse(400, "Ungültige Eingabe.")]
    [SwaggerResponse(500, "Interner Serverfehler.")]
    public async Task<IActionResult> RunBacktest(
        [FromQuery, Required]
        [SwaggerParameter(Description = "Startdatum (ISO 8601), z. B. `2025-01-01T00:00:00Z`")]
        DateTime startDate,
        [FromQuery, Required]
        [SwaggerParameter(Description = "Enddatum (ISO 8601), z. B. `2025-03-01T00:00:00Z`")]
        DateTime endDate,
        [FromQuery]
        [SwaggerParameter(Description = "Wenn `true`, werden erkannte Signale als BuyActionLogs/SellActionLogs persistiert (Signal-Backfill). Standard: `false`.")]
        bool persist,
        [FromQuery] double momentumBrakeMaxRatio = DistributionTopThresholds.MomentumBrakeMaxRatio,
        [FromQuery] double troughMinDrawdownPercent = DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
        [FromQuery] double troughRecoveryProximityPercent = DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
        [FromQuery] int bullishMinAlignedRanges = LongTimeframeThresholds.BullishMinAlignedRanges,
        [FromQuery] int bullishMaxAlignedRanges = int.MaxValue,
        [FromQuery] int bullishMinAlignedAnchorRanges = LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
        [FromQuery] int bullishMinTrendCountSum = LongTimeframeThresholds.BullishMinTrendCountSum,
        [FromQuery] int bullishMaxTrendCountSum = int.MaxValue,
        [FromQuery] int bearishMinAlignedRanges = LongTimeframeThresholds.BearishMinAlignedRanges,
        [FromQuery] int bearishMinTrendCountSum = LongTimeframeThresholds.BearishMinTrendCountSum,
        [FromQuery] double bearishRegimeMinLinearBullishScore = MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
        [FromQuery] int reEntryCooldownHours = -1,
        [FromQuery] int smaExitDebounceDays = 0,
        [FromQuery] bool bullishRegimeBlockBuy = false,
        [FromQuery] bool entryRequiresPriceAboveSma200 = false,
        [FromQuery] double breakevenTriggerPercent = 0.0,
        [FromQuery] double trailingStopPercent = 0.0,
        [FromQuery] double trailingStopActivationPercent = 0.0,
        [FromQuery] double nearAthSellSuppressMinPercent = 0.0,
        [FromQuery] int athProximityBuyBlockMaxAthAgeDays = 0,
        [FromQuery] bool macroOnlyMode = true,
        CancellationToken cancellationToken = default)
    {
        if (endDate <= startDate)
            return BadRequest("'endDate' muss nach 'startDate' liegen.");

        try
        {
            var confluenceThresholdSet = CreateConfluenceThresholdSet(
                bullishMinAlignedRanges,
                bullishMaxAlignedRanges,
                bullishMinAlignedAnchorRanges,
                bullishMinTrendCountSum,
                bullishMaxTrendCountSum,
                bearishMinAlignedRanges,
                bearishMinTrendCountSum);
            var result = await backtestService.RunBacktestAsync(startDate, endDate, cancellationToken, persist,
                momentumBrakeMaxRatio: momentumBrakeMaxRatio,
                troughMinDrawdownPercent: troughMinDrawdownPercent,
                troughRecoveryProximityPercent: troughRecoveryProximityPercent,
                confluenceThresholdSet: confluenceThresholdSet,
                bearishRegimeMinLinearBullishScore: bearishRegimeMinLinearBullishScore,
                reEntryCooldownHours: reEntryCooldownHours,
                smaExitDebounceDays: smaExitDebounceDays,
                bullishRegimeBlockBuy: bullishRegimeBlockBuy,
                entryRequiresPriceAboveSma200: entryRequiresPriceAboveSma200,
                breakevenTriggerPercent: breakevenTriggerPercent,
                trailingStopPercent: trailingStopPercent,
                trailingStopActivationPercent: trailingStopActivationPercent,
                nearAthSellSuppressMinPercent: nearAthSellSuppressMinPercent,
                athProximityBuyBlockMaxAthAgeDays: athProximityBuyBlockMaxAthAgeDays,
                macroOnlyMode: macroOnlyMode);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Fehler beim Backtesting: {ex.Message}");
        }
    }

    [HttpGet("split-validation")]
    [SwaggerOperation(
        Summary = "Out-of-Sample-Split-Validierung gegen Overfitting.",
        Description = @"
Teilt den Gesamtzeitraum an `splitDate` in zwei Hälften und führt den Backtest für beide separat aus:
- **In-Sample** (startDate → splitDate): der Zeitraum, auf dem die Strategie historisch kalibriert wurde.
- **Out-of-Sample** (splitDate → endDate): unbekannte Daten zum Zeitpunkt der Kalibrierung.

**Interpretation von `strategyVsHodlDegradation`:**
- Stark positiv (z. B. > 20 %): Strategie verliert OOS deutlich an Überrendite → Overfitting-Hinweis.
- Nahe 0 oder negativ: Strategie generalisiert gut auf unbekannte Daten.

Standard-Split: 2018-01-01 → 2024-01-01 (In-Sample) / 2024-01-01 → heute (Out-of-Sample).
")]
    [SwaggerResponse(200, "Split-Validierungsergebnis.", typeof(BacktestSplitValidationResult))]
    [SwaggerResponse(400, "Ungültige Eingabe.")]
    [SwaggerResponse(500, "Interner Serverfehler.")]
    public async Task<IActionResult> RunSplitValidation(
        [FromQuery, Required]
        [SwaggerParameter(Description = "Startdatum des gesamten Zeitraums (ISO 8601), z. B. `2018-01-01T00:00:00Z`")]
        DateTime startDate,
        [FromQuery, Required]
        [SwaggerParameter(Description = "Trennpunkt In-Sample / Out-of-Sample, z. B. `2024-01-01T00:00:00Z`")]
        DateTime splitDate,
        [FromQuery, Required]
        [SwaggerParameter(Description = "Enddatum des gesamten Zeitraums (ISO 8601), z. B. `2026-05-14T00:00:00Z`")]
        DateTime endDate,
        [FromQuery] bool macroOnlyMode = true,
        CancellationToken cancellationToken = default)
    {
        if (splitDate <= startDate)
            return BadRequest("'splitDate' muss nach 'startDate' liegen.");
        if (endDate <= splitDate)
            return BadRequest("'endDate' muss nach 'splitDate' liegen.");

        try
        {
            var result = await backtestService.RunSplitValidationAsync(startDate, splitDate, endDate, cancellationToken, macroOnlyMode);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Fehler bei der Split-Validierung: {ex.Message}");
        }
    }

    [HttpGet("monte-carlo")]
    [SwaggerOperation(
        Summary = "Monte-Carlo-Signifikanztest: ist das Backtest-Ergebnis ein Zufallstreffer?",
        Description = @"
Random-Entry Monte Carlo: laeuft den echten Backtest einmal, ermittelt die abgeschlossenen Deals
und deren Haltedauer und vergleicht die Performance mit `iterations` zufaellig gewaehlten
Buy-Zeitpunkten im gleichen Zeitfenster (mit identischer Haltedauer pro Trade).

Liefert pro Metrik (TotalReturn, CompoundedReturn, WinRate, MeanReturn) die Null-Verteilung
plus p-Wert: Anteil der Zufallslaeufe, die mindestens so gut sind wie die reale Strategie.

**Interpretation des p-Werts (Primaer: TotalReturn):**
- `p < 0.01` sehr signifikant, **kein** Zufallstreffer
- `p < 0.05` signifikant
- `0.05-0.20` schwacher Hinweis
- `0.20-0.50` nicht signifikant
- `p > 0.50` Zufallstreffer wahrscheinlicher als Edge

**Default:** Ohne `startDate`/`endDate` wird der maximal verfuegbare Kerzen-Zeitraum verwendet
(maximale statistische Power durch maximalen n).

**Hinweis:** Bei nur 2-3 Deals ist die Aussage struktur-bedingt schwach (n klein); selbst p<0.05
beweist keinen langfristigen Edge - dieser Test ist eine *notwendige*, keine *hinreichende* Bedingung.
")]
    [SwaggerResponse(200, "Monte-Carlo-Ergebnis.", typeof(BacktestMonteCarloResult))]
    [SwaggerResponse(400, "Ungueltige Eingabe.")]
    [SwaggerResponse(500, "Interner Serverfehler oder keine Deals im Zeitraum.")]
    public async Task<IActionResult> RunMonteCarlo(
        [FromQuery]
        [SwaggerParameter(Description = "Startdatum (ISO 8601). Wenn leer, wird der frueheste verfuegbare Kerzenzeitpunkt verwendet.")]
        DateTime? startDate,
        [FromQuery]
        [SwaggerParameter(Description = "Enddatum (ISO 8601). Wenn leer, wird der spaeteste verfuegbare Kerzenzeitpunkt verwendet.")]
        DateTime? endDate,
        [FromQuery]
        [SwaggerParameter(Description = "Anzahl Monte-Carlo-Iterationen. Default 10000.")]
        int iterations = 10000,
        [FromQuery]
        [SwaggerParameter(Description = "Optionaler Seed fuer deterministische Reproduzierbarkeit.")]
        int? seed = null,
        CancellationToken cancellationToken = default)
    {
        if (iterations <= 0 || iterations > 1_000_000)
            return BadRequest("'iterations' muss zwischen 1 und 1.000.000 liegen.");

        // Default-Range: maximaler verfuegbarer Kerzen-Zeitraum (max. statistische Power).
        DateTime effectiveStart, effectiveEnd;
        if (!startDate.HasValue || !endDate.HasValue)
        {
            var candles = await binanceKlineService.GetCandlesUpToAsync("2h", DateTime.UtcNow, cancellationToken);
            if (candles.Count == 0)
                return StatusCode(500, "Keine Kerzen verfuegbar, um Default-Range zu bestimmen.");
            effectiveStart = startDate ?? DateTime.SpecifyKind(candles[0].CloseTime, DateTimeKind.Utc);
            effectiveEnd = endDate ?? DateTime.SpecifyKind(candles[^1].CloseTime, DateTimeKind.Utc);
        }
        else
        {
            effectiveStart = startDate.Value;
            effectiveEnd = endDate.Value;
        }

        if (effectiveEnd <= effectiveStart)
            return BadRequest("'endDate' muss nach 'startDate' liegen.");

        try
        {
            var result = await monteCarloService.RunAsync(effectiveStart, effectiveEnd, iterations, seed, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(500, ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Fehler beim Monte-Carlo-Test: {ex.Message}");
        }
    }

    [HttpGet("regime-stability")]
    [SwaggerOperation(
        Summary = "Regime-Stabilitaetsanalyse: konsistente Performance ueber verschiedene Marktphasen?",
        Description = @"
Laeuft die Strategie mit **fixen Produktions-Parametern** (keine Per-Fenster-Optimierung) ueber
mehrere rollende Zeitfenster und prueft: Funktioniert der Edge konsistent in verschiedenen
Marktregimes (Bull, Bear, Recovery), oder war das Gesamtergebnis ein Glueckstreffer einer Periode?

**Kernfrage:** Haette man die Strategie zu verschiedenen Zeitpunkten deployt - waere sie
konsistent profitabel und besser als Buy-and-Hold gewesen?

**Interpretation:**
- Fenster **mit Trades**: War der Trade profitabel? Schlug er BnH?
- Fenster **ohne Trades + BnH negativ**: Strategie hat Crash korrekt gemieden (gutes Zeichen).
- Fenster **ohne Trades + BnH positiv**: Strategie hat Chance verpasst (schlechtes Zeichen).

**Caveat:** Bei sehr langen Haltedauern (1000+ Tage) ueberschreiten viele Trades das Fensterende
und tauchen als Abschluss nicht auf. Deshalb ist das Fazit strukturell vorsichtig zu lesen.

**Default:** windowMonths=30, stepMonths=12 ergibt ~7 Fenster ueber die volle BTC-Historie.
")]
    [SwaggerResponse(200, "Stabilitaetsergebnis.", typeof(RegimeStabilityResult))]
    [SwaggerResponse(400, "Ungueltige Eingabe.")]
    [SwaggerResponse(500, "Interner Serverfehler.")]
    public async Task<IActionResult> RunRegimeStability(
        [FromQuery]
        [SwaggerParameter(Description = "Startdatum. Wenn leer, wird der frueheste verfuegbare Kerzenzeitpunkt verwendet.")]
        DateTime? startDate,
        [FromQuery]
        [SwaggerParameter(Description = "Enddatum. Wenn leer, wird der spaeteste verfuegbare Kerzenzeitpunkt verwendet.")]
        DateTime? endDate,
        [FromQuery]
        [SwaggerParameter(Description = "Fensterlaenge in Monaten. Default 48 (4 Jahre) - laenger als die typische Haltedauer, damit Trades abschliessen koennen.")]
        int windowMonths = 48,
        [FromQuery]
        [SwaggerParameter(Description = "Schrittweite in Monaten. Default 12.")]
        int stepMonths = 12,
        CancellationToken cancellationToken = default)
    {
        if (windowMonths <= 0 || windowMonths > 120)
            return BadRequest("'windowMonths' muss zwischen 1 und 120 liegen.");
        if (stepMonths <= 0 || stepMonths > windowMonths)
            return BadRequest("'stepMonths' muss zwischen 1 und windowMonths liegen.");

        DateTime effectiveStart, effectiveEnd;
        if (!startDate.HasValue || !endDate.HasValue)
        {
            var candles = await binanceKlineService.GetCandlesUpToAsync("2h", DateTime.UtcNow, cancellationToken);
            if (candles.Count == 0)
                return StatusCode(500, "Keine Kerzen verfuegbar.");
            effectiveStart = startDate ?? DateTime.SpecifyKind(candles[0].CloseTime, DateTimeKind.Utc);
            effectiveEnd = endDate ?? DateTime.SpecifyKind(candles[^1].CloseTime, DateTimeKind.Utc);
        }
        else
        {
            effectiveStart = startDate.Value;
            effectiveEnd = endDate.Value;
        }

        if (effectiveEnd <= effectiveStart)
            return BadRequest("'endDate' muss nach 'startDate' liegen.");

        try
        {
            var result = await regimeStabilityService.RunAsync(
                effectiveStart, effectiveEnd, windowMonths, stepMonths, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Fehler bei der Regime-Stabilitaetsanalyse: {ex.Message}");
        }
    }

    [HttpGet("bootstrap")]
    [SwaggerOperation(
        Summary = "Stationary-Block-Bootstrap: Konfidenzintervall + Alpha-p-Wert.",
        Description = @"
Stationary Bootstrap (Politis & Romano 1994) auf den taeglichen Strategie- und BnH-Renditen
ueber das angegebene Zeitfenster. Resampled Bloecke geometrisch verteilter Laenge
(Mittel `meanBlockLength`, Default 20 Handelstage), so dass die Autokorrelationsstruktur
der Crypto-Renditen erhalten bleibt.

Antwortet drei Fragen, die der Random-Entry-Monte-Carlo nicht beantwortet:
1. Wie breit ist das **Konfidenzintervall** der Strategie-Performance unter der real
   beobachteten Volatilitaet und Autokorrelation?
2. Wie breit ist es fuer Buy-and-Hold im gleichen Zeitraum?
3. Wie hoch ist die Wahrscheinlichkeit (`alphaPValue`), dass das realisierte Alpha
   = Strategie-BnH allein durch zufaellige Block-Resequenzierung der gleichen
   Tagesrenditen entsteht?

**Interpretation `alphaPValue`:**
- `p < 0.05`: Alpha auch unter Resampling robust positiv (statistisch signifikant)
- `p > 0.50`: Alpha nicht von zufaelliger Block-Anordnung der Tagesrenditen unterscheidbar
- Werte dazwischen: schwacher Hinweis

**Grenzen:** Bootstrap testet Robustheit *auf den real beobachteten Renditen*. Er sagt nichts
ueber Generalisierung auf nicht-gesehene Marktregimes (dafuer Regime-Stability) oder
ueber Zufallseinstiege (dafuer Monte-Carlo).
")]
    [SwaggerResponse(200, "Bootstrap-Ergebnis.", typeof(BacktestBootstrapResult))]
    [SwaggerResponse(400, "Ungueltige Eingabe.")]
    [SwaggerResponse(500, "Interner Serverfehler.")]
    public async Task<IActionResult> RunBootstrap(
        [FromQuery]
        [SwaggerParameter(Description = "Startdatum (ISO 8601). Wenn leer, wird der frueheste verfuegbare Kerzenzeitpunkt verwendet.")]
        DateTime? startDate,
        [FromQuery]
        [SwaggerParameter(Description = "Enddatum (ISO 8601). Wenn leer, wird der spaeteste verfuegbare Kerzenzeitpunkt verwendet.")]
        DateTime? endDate,
        [FromQuery]
        [SwaggerParameter(Description = "Anzahl Bootstrap-Iterationen. Default 5000.")]
        int iterations = 5000,
        [FromQuery]
        [SwaggerParameter(Description = "Mittlere Blocklaenge in Handelstagen (geometrische Verteilung). Default 20 (~1 Monat).")]
        int meanBlockLength = 20,
        [FromQuery]
        [SwaggerParameter(Description = "Optionaler Seed fuer deterministische Reproduzierbarkeit.")]
        int? seed = null,
        CancellationToken cancellationToken = default)
    {
        if (iterations <= 0 || iterations > 100_000)
            return BadRequest("'iterations' muss zwischen 1 und 100.000 liegen.");
        if (meanBlockLength < 1 || meanBlockLength > 365)
            return BadRequest("'meanBlockLength' muss zwischen 1 und 365 liegen.");

        DateTime effectiveStart, effectiveEnd;
        if (!startDate.HasValue || !endDate.HasValue)
        {
            var candles = await binanceKlineService.GetCandlesUpToAsync("1d", DateTime.UtcNow, cancellationToken);
            if (candles.Count == 0)
                return StatusCode(500, "Keine Kerzen verfuegbar, um Default-Range zu bestimmen.");
            effectiveStart = startDate ?? DateTime.SpecifyKind(candles[0].CloseTime, DateTimeKind.Utc);
            effectiveEnd = endDate ?? DateTime.SpecifyKind(candles[^1].CloseTime, DateTimeKind.Utc);
        }
        else
        {
            effectiveStart = startDate.Value;
            effectiveEnd = endDate.Value;
        }

        if (effectiveEnd <= effectiveStart)
            return BadRequest("'endDate' muss nach 'startDate' liegen.");

        try
        {
            var result = await stationaryBootstrapService.RunAsync(
                effectiveStart, effectiveEnd, iterations, meanBlockLength, seed, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(500, ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Fehler beim Bootstrap: {ex.Message}");
        }
    }

    [HttpGet("gate-ablation")]
    [SwaggerOperation(
        Summary = "Gate-Ablation-Sweep: misst den OOS-Effekt einzelner Gates.",
        Description = @"
Führt für jedes aktive Gate separat eine Split-Validierung durch: alle anderen Gates bleiben auf ihren Produktionswerten (appsettings), nur das eine Gate wird deaktiviert.

**Interpretation von `oosDeltaChangeVsBaseline`:**
- **Positiv (> 5 %)**: Deaktivieren verbessert OOS → Gate schadet der Generalisierung → `RemoveCandidate`.
- **Negativ (< -5 %)**: Deaktivieren verschlechtert OOS → Gate hilft wirklich → `KeepGate`.
- **Neutral (±5 %)**: Gate hat keinen signifikanten OOS-Effekt.

Streamt Ergebnisse (erste Zeile = Baseline mit allen aktiven Gates).
")]
    [SwaggerResponse(200, "Gate-Ablation-Ergebnisse (gestreamt).", typeof(IAsyncEnumerable<BacktestGateAblationRow>))]
    public IAsyncEnumerable<BacktestGateAblationRow> RunGateAblation(
        [FromQuery, Required]
        [SwaggerParameter(Description = "Startdatum des gesamten Zeitraums (ISO 8601), z. B. `2018-01-01T00:00:00Z`")]
        DateTime startDate,
        [FromQuery, Required]
        [SwaggerParameter(Description = "Trennpunkt In-Sample / Out-of-Sample, z. B. `2024-01-01T00:00:00Z`")]
        DateTime splitDate,
        [FromQuery, Required]
        [SwaggerParameter(Description = "Enddatum des gesamten Zeitraums (ISO 8601), z. B. `2026-05-14T00:00:00Z`")]
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        return backtestService.RunGateAblationAsync(startDate, splitDate, endDate, cancellationToken);
    }

    [HttpGet("sweep")]
    [SwaggerOperation(
        Summary = "Grid-Sweep über DistributionTopFilter-Thresholds.",
        Description = "Führt den Backtest für alle Kombinationen der angegebenen Threshold-Grids durch. Streamt Ergebnisse als JSON-Array (IAsyncEnumerable). Der IMemoryCache reduziert DB-Calls ab der 2. Kombination drastisch.")]
    public IAsyncEnumerable<BacktestSweepRow> RunSweep(
        [FromQuery, Required] DateTime startDate,
        [FromQuery, Required] DateTime endDate,
        [FromQuery] string momentumBrakeRatios = "0.0,0.20,0.25,0.35,0.50",
        [FromQuery] string troughMinDrawdownPercents = "5.0,8.0,10.0,15.0",
        [FromQuery] string troughRecoveryProximityPercents = "3.0,5.0,8.0,12.0",
        [FromQuery] string bullishMinAlignedRanges = "3",
        [FromQuery] string bullishMaxAlignedRanges = "2147483647",
        [FromQuery] string bullishMinAlignedAnchorRanges = "2",
        [FromQuery] string bullishMinTrendCountSums = "1",
        [FromQuery] string bullishMaxTrendCountSums = "2147483647",
        [FromQuery] string bearishMinAlignedRanges = "3",
        [FromQuery] string bearishMinTrendCountSums = "2",
        [FromQuery] string bearishRegimeMinLinearBullishScores = "22.0",
        [FromQuery] string bullishRegimeBlockBuys = "false",
        [FromQuery] string entryRequiresPriceAboveSma200s = "false",
        [FromQuery] string breakevenTriggerPercents = "0.0",
        CancellationToken cancellationToken = default)
    {
        return backtestService.SweepThresholdsAsync(
            startDate, endDate,
            ParseDoubles(momentumBrakeRatios),
            ParseDoubles(troughMinDrawdownPercents),
            ParseDoubles(troughRecoveryProximityPercents),
            ParseInts(bullishMinAlignedRanges),
            ParseInts(bullishMaxAlignedRanges),
            ParseInts(bullishMinAlignedAnchorRanges),
            ParseInts(bullishMinTrendCountSums),
            ParseInts(bullishMaxTrendCountSums),
            ParseInts(bearishMinAlignedRanges),
            ParseInts(bearishMinTrendCountSums),
            ParseDoubles(bearishRegimeMinLinearBullishScores),
            ParseBools(bullishRegimeBlockBuys),
            ParseBools(entryRequiresPriceAboveSma200s),
            ParseDoubles(breakevenTriggerPercents),
            cancellationToken);
    }

    [HttpGet("walkforward")]
    [SwaggerOperation(
        Summary = "Walk-Forward-Analyse über Threshold-Grids.",
        Description = "Optimiert je Fold auf einem Train-Fenster per Sweep und validiert das beste Param-Set auf dem nachfolgenden Test-Fenster.")]
    public IAsyncEnumerable<BacktestWalkForwardFoldRow> RunWalkForward(
        [FromQuery, Required] DateTime startDate,
        [FromQuery, Required] DateTime endDate,
        [FromQuery] int trainMonths = 12,
        [FromQuery] int testMonths = 3,
        [FromQuery] int stepMonths = 1,
        [FromQuery] string momentumBrakeRatios = "0.0,0.20,0.25,0.35,0.50",
        [FromQuery] string troughMinDrawdownPercents = "5.0,8.0,10.0,15.0",
        [FromQuery] string troughRecoveryProximityPercents = "3.0,5.0,8.0,12.0",
        [FromQuery] string bullishMinAlignedRanges = "3",
        [FromQuery] string bullishMaxAlignedRanges = "2147483647",
        [FromQuery] string bullishMinAlignedAnchorRanges = "2",
        [FromQuery] string bullishMinTrendCountSums = "1",
        [FromQuery] string bullishMaxTrendCountSums = "2147483647",
        [FromQuery] string bearishMinAlignedRanges = "3",
        [FromQuery] string bearishMinTrendCountSums = "2",
        [FromQuery] string bearishRegimeMinLinearBullishScores = "22.0",
        [FromQuery] string bullishRegimeBlockBuys = "false",
        [FromQuery] string entryRequiresPriceAboveSma200s = "false",
        [FromQuery] string breakevenTriggerPercents = "0.0",
        CancellationToken cancellationToken = default)
    {
        return backtestService.WalkForwardAsync(
            startDate,
            endDate,
            trainMonths,
            testMonths,
            stepMonths,
            ParseDoubles(momentumBrakeRatios),
            ParseDoubles(troughMinDrawdownPercents),
            ParseDoubles(troughRecoveryProximityPercents),
            ParseInts(bullishMinAlignedRanges),
            ParseInts(bullishMaxAlignedRanges),
            ParseInts(bullishMinAlignedAnchorRanges),
            ParseInts(bullishMinTrendCountSums),
            ParseInts(bullishMaxTrendCountSums),
            ParseInts(bearishMinAlignedRanges),
            ParseInts(bearishMinTrendCountSums),
            ParseDoubles(bearishRegimeMinLinearBullishScores),
            ParseBools(bullishRegimeBlockBuys),
            ParseBools(entryRequiresPriceAboveSma200s),
            ParseDoubles(breakevenTriggerPercents),
            cancellationToken);
    }

    [HttpGet("diagnostics")]
    [SwaggerOperation(
        Summary = "Diagnose der Entscheidungs- und Blocking-Pfade.",
        Description = "Analysiert den Backtest-Zeitraum ohne Persistenz und zählt Signalquellen, Regime, Blocker und ausgeführte Trades.")]
    [SwaggerResponse(200, "Diagnose-Ergebnis.", typeof(BacktestDecisionDiagnostics))]
    [SwaggerResponse(400, "Ungültige Eingabe.")]
    [SwaggerResponse(500, "Interner Serverfehler.")]
    public async Task<IActionResult> Diagnostics(
        [FromQuery, Required] DateTime startDate,
        [FromQuery, Required] DateTime endDate,
        [FromQuery] double momentumBrakeMaxRatio = DistributionTopThresholds.MomentumBrakeMaxRatio,
        [FromQuery] double troughMinDrawdownPercent = DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
        [FromQuery] double troughRecoveryProximityPercent = DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
        [FromQuery] int bullishMinAlignedRanges = LongTimeframeThresholds.BullishMinAlignedRanges,
        [FromQuery] int bullishMaxAlignedRanges = int.MaxValue,
        [FromQuery] int bullishMinAlignedAnchorRanges = LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
        [FromQuery] int bullishMinTrendCountSum = LongTimeframeThresholds.BullishMinTrendCountSum,
        [FromQuery] int bullishMaxTrendCountSum = int.MaxValue,
        [FromQuery] int bearishMinAlignedRanges = LongTimeframeThresholds.BearishMinAlignedRanges,
        [FromQuery] int bearishMinTrendCountSum = LongTimeframeThresholds.BearishMinTrendCountSum,
        [FromQuery] double bearishRegimeMinLinearBullishScore = MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
        CancellationToken cancellationToken = default)
    {
        if (endDate <= startDate)
            return BadRequest("'endDate' muss nach 'startDate' liegen.");

        try
        {
            var confluenceThresholdSet = CreateConfluenceThresholdSet(
                bullishMinAlignedRanges,
                bullishMaxAlignedRanges,
                bullishMinAlignedAnchorRanges,
                bullishMinTrendCountSum,
                bullishMaxTrendCountSum,
                bearishMinAlignedRanges,
                bearishMinTrendCountSum);
            var result = await backtestService.GetDecisionDiagnosticsAsync(
                startDate,
                endDate,
                cancellationToken,
                momentumBrakeMaxRatio,
                troughMinDrawdownPercent,
                troughRecoveryProximityPercent,
                confluenceThresholdSet,
                bearishRegimeMinLinearBullishScore);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Fehler bei der Backtest-Diagnose: {ex.Message}");
        }
    }

    [HttpGet("signal-returns")]
    [SwaggerOperation(
        Summary = "Rohe BUY-Signale mit Forward-Returns.",
        Description = "Gibt für jeden Timestamp, an dem das Confluence-Modell ein BUY-Signal emittiert (vor jeglichem Execution-Gating), die Kennzahlen (AR, TC, Regime) und Forward-Returns bei +5/14/30/90/180 Tagen zurück. Ermöglicht robuste Korrelationsanalyse mit hunderten Datenpunkten.")]
    [SwaggerResponse(200, "Analyse-Ergebnis.", typeof(BuySignalForwardReturnAnalysis))]
    [SwaggerResponse(400, "Ungültige Eingabe.")]
    [SwaggerResponse(500, "Interner Serverfehler.")]
    public async Task<IActionResult> SignalReturns(
        [FromQuery, Required] DateTime startDate,
        [FromQuery, Required] DateTime endDate,
        [FromQuery] int bullishMinAlignedRanges = LongTimeframeThresholds.BullishMinAlignedRanges,
        [FromQuery] int bullishMaxAlignedRanges = int.MaxValue,
        [FromQuery] int bullishMinAlignedAnchorRanges = LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
        [FromQuery] int bullishMinTrendCountSum = LongTimeframeThresholds.BullishMinTrendCountSum,
        [FromQuery] int bullishMaxTrendCountSum = int.MaxValue,
        [FromQuery] int bearishMinAlignedRanges = LongTimeframeThresholds.BearishMinAlignedRanges,
        [FromQuery] int bearishMinTrendCountSum = LongTimeframeThresholds.BearishMinTrendCountSum,
        CancellationToken cancellationToken = default)
    {
        if (endDate <= startDate)
            return BadRequest("'endDate' muss nach 'startDate' liegen.");

        try
        {
            var confluenceThresholdSet = CreateConfluenceThresholdSet(
                bullishMinAlignedRanges,
                bullishMaxAlignedRanges,
                bullishMinAlignedAnchorRanges,
                bullishMinTrendCountSum,
                bullishMaxTrendCountSum,
                bearishMinAlignedRanges,
                bearishMinTrendCountSum);
            var result = await backtestService.GetBuySignalForwardReturnsAsync(
                startDate,
                endDate,
                confluenceThresholdSet,
                cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Fehler bei der Signal-Returns-Analyse: {ex.Message}");
        }
    }

    private static IReadOnlyList<double> ParseDoubles(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
            .ToList();

    private static IReadOnlyList<int> ParseInts(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => int.Parse(v, CultureInfo.InvariantCulture))
            .ToList();

    private static IReadOnlyList<bool> ParseBools(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => bool.Parse(v.Trim()))
            .ToList();

    private static LongTimeframeConfluenceThresholdSet CreateConfluenceThresholdSet(
        int bullishMinAlignedRanges,
        int bullishMaxAlignedRanges,
        int bullishMinAlignedAnchorRanges,
        int bullishMinTrendCountSum,
        int bullishMaxTrendCountSum,
        int bearishMinAlignedRanges,
        int bearishMinTrendCountSum)
    {
        return new LongTimeframeConfluenceThresholdSet(
            bullishMinAlignedRanges: bullishMinAlignedRanges,
            bullishMaxAlignedRanges: bullishMaxAlignedRanges,
            bullishMinAlignedAnchorRanges: bullishMinAlignedAnchorRanges,
            bullishMinTrendCountSum: bullishMinTrendCountSum,
            bullishMaxTrendCountSum: bullishMaxTrendCountSum,
            bearishMinAlignedRanges: bearishMinAlignedRanges,
            bearishMinTrendCountSum: bearishMinTrendCountSum);
    }
}
