using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using BtcAdvisor.Constants;
using BtcAdvisor.Repository;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace BtcAdvisor.Controllers;

[ApiController]
[Route("[controller]")]
public class BitcoinController(
    ITradingRepository tradingRepository,
    ITradeOpportunityService tradeOpportunityService,
    IBinanceKlineService binanceKlineService,
    IOptions<BearMarketFilterOptions> bearMarketFilterOptions) : ControllerBase, IBitcoinController
{

    private sealed class BitcoinPricePointDto
    {
        public DateTime MaxDateTime { get; set; }
        public double CurrentPrice { get; set; }
    }

    private sealed class BitcoinPriceCoverageDto
    {
        public DateTime MinDateTime { get; set; }
        public DateTime MaxDateTime { get; set; }
        public int Count { get; set; }
    }

[HttpGet("current")]
[HttpGet("/current")]
[SwaggerOperation(
    Summary = "Gibt den aktuellen BTC-Preis und die letzte Kauf-/Verkaufsposition zurück.",
    Description = "Liefert den aktuellen Bitcoin-Preis aus dem Binance-Cache sowie die letzte Buy- und Sell-Action aus der Datenbank."
)]
[SwaggerResponse(200, "Aktueller BTC-Zustand.")]
public async Task<IActionResult> GetBitcoinInfo(CancellationToken cancellationToken)
{
    var now = DateTime.UtcNow;
    var (latestSell, latestBuy) = await tradingRepository.FindLatestAction(now);
    var candles = await binanceKlineService.GetCandlesUpToAsync("1h", now, cancellationToken);
    var price = candles.LastOrDefault()?.Close;
    return Ok(new
    {
        TradingDate = now,
        Price = price,
        LatestBuy = latestBuy,
        LatestSell = latestSell
    });
}

    [HttpGet("forecast")]
    [HttpGet("/forecast")]
    [SwaggerOperation(
        Summary = "Grobe Prognose fuer das naechste Buy- oder Sell-Signal.",
        Description = @"
Liefert eine deterministische Forecast-Ansicht des MacroOnly-Algorithmus:

- **Sell-Side** (CycleTopZone DistributionSell):
  - Post-Halving-Fenster [MinMonths, MaxMonths] nach letztem Halving.
  - ATH-Proximity (aktueller Preis ≥ X % vom Cycle-ATH).
  - Drawup (Cycle-ATH ≥ Y % ueber Cycle-Low).
- **Buy-Side** (DeepCycleLow StrongBuy):
  - Adaptive Drawdown-Schwelle vom Cycle-ATH (FormulaByCycle / PreviousCycleMinusDelta).

Das Feld `NextSignalKind` zeigt den naechsten zu erwartenden Trigger basierend auf der aktuellen Position (offen → Sell-Forecast, geschlossen → Buy-Forecast)."
    )]
    [SwaggerResponse(200, "Forecast erfolgreich berechnet.")]
    public async Task<IActionResult> GetForecast(CancellationToken cancellationToken)
    {
        var asOf = DateTime.UtcNow;
        var opts = bearMarketFilterOptions.Value;

        var (latestSell, latestBuy) = await tradingRepository.FindLatestAction(asOf);
        var hasOpenPosition = latestBuy is not null
            && (latestSell is null || latestBuy.TradingDate > latestSell.TradingDate);

        // 1h candle for fresh "current price" display.
        var hourlyCandles = await binanceKlineService.GetCandlesUpToAsync("1h", asOf, cancellationToken);
        double? currentPrice = hourlyCandles.LastOrDefault()?.Close;

        // 1d candles bounded by CycleTopSellZoneLookbackDays — mirrors IsCycleTopOvervaluedAsync.
        var sellInterval = string.IsNullOrWhiteSpace(opts.CycleTopSellZoneInterval) ? "1d" : opts.CycleTopSellZoneInterval;
        var lookbackDays = opts.CycleTopSellZoneLookbackDays > 0 ? opts.CycleTopSellZoneLookbackDays : 1460;
        var dailyCandles = await binanceKlineService.GetCandlesUpToAsync(sellInterval, asOf, cancellationToken);
        var cycleStart = asOf.AddDays(-lookbackDays);
        var windowCandles = dailyCandles
            .Where(c => c.CloseTime <= asOf && c.CloseTime >= cycleStart)
            .ToList();
        double cycleAth = windowCandles.Count > 0 ? windowCandles.Max(c => c.High) : 0.0;
        double cycleLow = windowCandles.Count > 0 ? windowCandles.Min(c => c.Low) : 0.0;

        // Halving window bounds (30.44-day months — same approximation as IsWithinPostHalvingWindow).
        DateTime? latestHalving = null;
        foreach (var h in BitcoinConstants.Halvings)
        {
            if (h <= asOf && (latestHalving is null || h > latestHalving.Value))
            {
                latestHalving = h;
            }
        }
        DateTime? windowOpenUtc = latestHalving?.AddDays(opts.CycleTopSellZoneMinMonthsAfterHalving * 30.44);
        DateTime? windowCloseUtc = latestHalving?.AddDays(opts.CycleTopSellZoneMaxMonthsAfterHalving * 30.44);
        double? monthsSinceHalving = latestHalving is null ? null : (asOf - latestHalving.Value).TotalDays / 30.44;
        var withinHalvingWindow = BitcoinConstants.IsWithinPostHalvingWindow(
            asOf,
            opts.CycleTopSellZoneMinMonthsAfterHalving,
            opts.CycleTopSellZoneMaxMonthsAfterHalving);

        // Sell-side metrics
        var minProximityPct = opts.CycleTopSellZoneMinAthProximityPercent;
        var minDrawupPct = opts.CycleTopSellZoneMinDrawupPercent;
        double athProximityPct = cycleAth > 0 && currentPrice.HasValue
            ? currentPrice.Value / cycleAth * 100.0 : 0.0;
        double drawupPct = cycleLow > 0 && currentPrice.HasValue
            ? (currentPrice.Value - cycleLow) / cycleLow * 100.0 : 0.0;
        double sellProximityPriceThreshold = cycleAth * (minProximityPct / 100.0);
        double sellDrawupPriceThreshold = cycleLow * (1.0 + minDrawupPct / 100.0);
        double sellTriggerPrice = Math.Max(sellProximityPriceThreshold, sellDrawupPriceThreshold);

        // Buy-side metrics (DeepCycleLow with adaptive threshold).
        var drawdownThresholdPct = AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(opts, asOf);
        double currentDrawdownPct = cycleAth > 0 && currentPrice.HasValue
            ? (currentPrice.Value - cycleAth) / cycleAth * 100.0 : 0.0;
        double buyTriggerPrice = cycleAth * (1.0 - drawdownThresholdPct / 100.0);
        int cycleNumber = AthDrawdownThresholdPolicy.ResolveCycleNumber(asOf, opts.AthDrawdownAdaptiveCycleStartYears);

        string nextSignalKind;
        string rationale;
        if (hasOpenPosition)
        {
            nextSignalKind = "Sell";
            var blockers = new List<string>();
            if (!withinHalvingWindow)
            {
                if (windowOpenUtc.HasValue && asOf < windowOpenUtc.Value)
                {
                    blockers.Add($"Halving-Fenster oeffnet erst {windowOpenUtc:yyyy-MM-dd}");
                }
                else if (windowCloseUtc.HasValue && asOf > windowCloseUtc.Value)
                {
                    blockers.Add($"Halving-Fenster bereits geschlossen ({windowCloseUtc:yyyy-MM-dd})");
                }
            }
            if (athProximityPct < minProximityPct)
            {
                blockers.Add($"ATH-Naehe {athProximityPct:N1}% < Schwelle {minProximityPct:N1}%");
            }
            if (drawupPct < minDrawupPct)
            {
                blockers.Add($"Drawup {drawupPct:N1}% < Schwelle {minDrawupPct:N1}%");
            }
            rationale = blockers.Count == 0
                ? "Alle Sell-Bedingungen erfuellt — Confirmation-Fenster (1 Tag) verbleibt."
                : "Blocker: " + string.Join("; ", blockers);
        }
        else
        {
            nextSignalKind = "Buy";
            var blockers = new List<string>();
            if (!opts.AthDrawdownOverrideEnabled)
            {
                blockers.Add("AthDrawdownOverride deaktiviert");
            }
            if (currentDrawdownPct > -drawdownThresholdPct)
            {
                blockers.Add($"Drawdown {currentDrawdownPct:N1}% > Trigger -{drawdownThresholdPct:N1}%");
            }
            rationale = blockers.Count == 0
                ? "DeepCycleLow-Bedingung erfuellt — Buy beim naechsten Tick erwartet."
                : "Blocker: " + string.Join("; ", blockers);
        }

        return Ok(new
        {
            AsOfUtc = asOf,
            CurrentPrice = currentPrice,
            CycleAth = cycleAth,
            CycleLow = cycleLow,
            HasOpenPosition = hasOpenPosition,
            NextSignalKind = nextSignalKind,
            Rationale = rationale,
            SellSide = new
            {
                Enabled = opts.CycleTopSellZoneEnabled,
                HalvingDateUtc = latestHalving,
                WithinHalvingWindow = withinHalvingWindow,
                HalvingWindowOpenUtc = windowOpenUtc,
                HalvingWindowCloseUtc = windowCloseUtc,
                MonthsSinceHalving = monthsSinceHalving,
                DaysUntilWindowOpens = windowOpenUtc.HasValue
                    ? Math.Max(0.0, (windowOpenUtc.Value - asOf).TotalDays) : (double?)null,
                DaysUntilWindowCloses = windowCloseUtc.HasValue
                    ? Math.Max(0.0, (windowCloseUtc.Value - asOf).TotalDays) : (double?)null,
                AthProximityPercent = athProximityPct,
                AthProximityThresholdPercent = minProximityPct,
                DrawupPercent = drawupPct,
                DrawupThresholdPercent = minDrawupPct,
                TriggerPriceThreshold = sellTriggerPrice,
                ConfirmationDays = opts.CycleTopSellZoneConfirmationDays
            },
            BuySide = new
            {
                Enabled = opts.AthDrawdownOverrideEnabled,
                AdaptiveMode = opts.AthDrawdownAdaptiveMode,
                CycleNumber = cycleNumber,
                DrawdownThresholdPercent = drawdownThresholdPct,
                CurrentDrawdownPercent = currentDrawdownPct,
                TriggerPriceThreshold = buyTriggerPrice
            }
        });
    }

    [HttpGet("prices")]
    [HttpGet("/prices")]
    [SwaggerOperation(
        Summary = "Gibt echte BTC-Preisdaten (Candles) fuer einen Zeitbereich zurueck.",
        Description = @"
Liefert BTC-Kerzendaten auf Basis der lokal gecachten Binance-Klines.

**Hinweise:**
- `start` und `end` im ISO-8601-Format (UTC empfohlen)
- `interval` default: `1h`
- Unterstuetzte Intervalle: `5m`, `15m`, `30m`, `1h`, `2h`, `4h`, `12h`, `1d`, `1w`
"
    )]
    [SwaggerResponse(200, "Historische BTC-Preisdaten.")]
    [SwaggerResponse(400, "Ungueltige Eingabe.")]
    public async Task<IActionResult> GetBitcoinPrices(
        [FromQuery, Required] DateTime start,
        [FromQuery, Required] DateTime end,
        [FromQuery] string interval = "1h",
        CancellationToken cancellationToken = default)
    {
        if (end <= start)
        {
            return BadRequest("'end' muss nach 'start' liegen.");
        }

        var supportedIntervals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "5m", "15m", "30m", "1h", "2h", "4h", "12h", "1d", "1w"
        };

        if (!supportedIntervals.Contains(interval))
        {
            return BadRequest("Ungueltiges 'interval'. Erlaubt: 5m, 15m, 30m, 1h, 2h, 4h, 12h, 1d, 1w.");
        }

        var startUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(end, DateTimeKind.Utc);
        var candles = await binanceKlineService.GetCandlesRangeAsync(interval.ToLowerInvariant(), startUtc, endUtc, cancellationToken);

        var points = candles
            .Select(c => new BitcoinPricePointDto
            {
                MaxDateTime = c.CloseTime,
                CurrentPrice = c.Close
            })
            .ToList();

        return Ok(points);
    }

    [HttpGet("prices/coverage")]
    [HttpGet("/prices/coverage")]
    [SwaggerOperation(
        Summary = "Gibt den verfuegbaren BTC-Preisbereich fuer ein Intervall zurueck.",
        Description = @"
Liefert den aktuell verfuegbaren Zeitbereich (Min/Max) der lokal gecachten BTC-Kerzen.

**Hinweise:**
- `interval` default: `1h`
- Unterstuetzte Intervalle: `5m`, `15m`, `30m`, `1h`, `2h`, `4h`, `12h`, `1d`, `1w`
"
    )]
    [SwaggerResponse(200, "Verfuegbarer Bereich wurde ermittelt.")]
    [SwaggerResponse(400, "Ungueltige Eingabe.")]
    [SwaggerResponse(404, "Keine Kerzen fuer das Intervall gefunden.")]
    public async Task<IActionResult> GetBitcoinPriceCoverage(
        [FromQuery] string interval = "1h",
        CancellationToken cancellationToken = default)
    {
        var supportedIntervals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "5m", "15m", "30m", "1h", "2h", "4h", "12h", "1d", "1w"
        };

        if (!supportedIntervals.Contains(interval))
        {
            return BadRequest("Ungueltiges 'interval'. Erlaubt: 5m, 15m, 30m, 1h, 2h, 4h, 12h, 1d, 1w.");
        }

        // Pull complete cached history for interval and expose available min/max range.
        var candles = await binanceKlineService.GetCandlesUpToAsync(interval.ToLowerInvariant(), DateTime.UtcNow, cancellationToken);
        if (candles.Count == 0)
        {
            return NotFound("Keine BTC-Preisdaten fuer das angefragte Intervall gefunden.");
        }

        var ordered = candles
            .OrderBy(c => c.CloseTime)
            .ToList();

        var coverage = new BitcoinPriceCoverageDto
        {
            MinDateTime = ordered[0].OpenTime,
            MaxDateTime = ordered[^1].CloseTime,
            Count = ordered.Count
        };

        return Ok(coverage);
    }

[HttpPost("rerun")]
[HttpPost("/rerun")]
    [SwaggerOperation(
        Summary = "Berechnet den TradeAdvice für einen bestimmten Zeitbereich neu.",
        Description = @"
Löscht bestehende Sell-/BuyActionLogs im angegebenen Bereich und berechnet neue TradeAdvice-Daten für jeden Zeitstempel im Bereich.

**Hinweise:**
- Die Datumsangaben müssen im **ISO 8601-Format** vorliegen (z. B. `2026-02-01T00:00:00Z`).
- `endDate` muss **nach** `startDate` liegen.
- Beide Parameter sind **erforderlich**.
"
    )]
    [SwaggerResponse(200, "TradeAdvice wurde erfolgreich neu berechnet.")]
    [SwaggerResponse(400, "Ungültige Eingabe (z. B. `endDate` vor `startDate`).")]
    [SwaggerResponse(500, "Interner Serverfehler.")]
    public async Task<IActionResult> ReRunTradeAdvice(
        [FromQuery, Required]
        [SwaggerParameter(
            Description = @"
Startdatum des Zeitbereichs (ISO 8601-Format).
**Beispiel:** `2026-02-01T00:00:00Z`
            ")]
        DateTime startDate,
        [FromQuery, Required]
        [SwaggerParameter(
            Description = @"
Enddatum des Zeitbereichs (ISO 8601-Format).
**Beispiel:** `2026-02-02T00:00:00Z`
            ")]
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"ReRunTradeAdvice called for range {startDate:o} to {endDate:o}");
            // No outer transaction: keeping a DB connection open for 1440+ minutes causes Npgsql stream timeouts.
            // Each TradeOpportunity call manages its own atomic DB write.
            await tradingRepository.DeleteSellBuyActionLogsInRange(startDate, endDate, cancellationToken);
            Console.WriteLine("Existing Sell/BuyActionLogs deleted.");
            try
            {
                // 2. Neue TradeAdvice-Daten für jeden Zeitstempel im Bereich berechnen
                var currentDate = startDate;
                while (currentDate <= endDate)
                {
                    var candles = await binanceKlineService.GetCandlesUpToAsync("1h", currentDate, cancellationToken);
                    var lastCandle = candles.LastOrDefault();
                    if (lastCandle is null || lastCandle.Close <= 0)
                    {
                        Console.WriteLine($"Skipping {currentDate:o}: no Binance price available.");
                        currentDate = currentDate.AddMinutes(1);
                        continue;
                    }

                    var deal = new Deal
                    {
                        TradeAdvice = "MacroOnly",
                        Price = (decimal)lastCandle.Close,
                        TradingDate = currentDate
                    };
                    Console.WriteLine($"Processing deal for {currentDate:o}: Price: {deal.Price}");
                    await tradeOpportunityService.TradeOpportunity(deal, cancellationToken);
                    currentDate = currentDate.AddMinutes(1);
                }

                return Ok($"TradeAdvice für {startDate} bis {endDate} erfolgreich neu berechnet.");
            }
            catch (Exception ex)
            {
                return BadRequest($"Fehler beim Neuberechnen: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Interner Serverfehler: {ex.Message}");
        }
    }

}
