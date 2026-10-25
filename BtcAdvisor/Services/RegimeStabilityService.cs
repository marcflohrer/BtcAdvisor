using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

/// <summary>
/// Testet die Strategie mit den fixen Produktions-Defaults ueber mehrere rollende Zeitfenster
/// und beantwortet: "Ist der Edge konsistent ueber verschiedene Marktphasen, oder war es Glueck?"
///
/// Methodologie:
/// - Feste Parameter (identisch zu GET /backtest ohne Overrides): kein Per-Fold-Optimierung.
/// - Jedes Fenster ist strukturell unabhaengig und deckt andere Marktregimes ab.
/// - Fenster ohne abgeschlossene Trades sind informativer Kontext (nicht ignoriert).
/// </summary>
public class RegimeStabilityService(
    IBacktestService backtestService) : IRegimeStabilityService
{
    public async Task<RegimeStabilityResult> RunAsync(
        DateTime startDate,
        DateTime endDate,
        int windowMonths,
        int stepMonths,
        CancellationToken cancellationToken)
    {
        if (endDate <= startDate) throw new ArgumentException("endDate must be after startDate");
        if (windowMonths <= 0) throw new ArgumentException("windowMonths must be positive");
        if (stepMonths <= 0) throw new ArgumentException("stepMonths must be positive");

        var confluenceThresholdSet = new LongTimeframeConfluenceThresholdSet(
            bullishMinAlignedRanges: LongTimeframeThresholds.BullishMinAlignedRanges,
            bullishMaxAlignedRanges: int.MaxValue,
            bullishMinAlignedAnchorRanges: LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
            bullishMinTrendCountSum: LongTimeframeThresholds.BullishMinTrendCountSum,
            bullishMaxTrendCountSum: int.MaxValue,
            bearishMinAlignedRanges: LongTimeframeThresholds.BearishMinAlignedRanges,
            bearishMinTrendCountSum: LongTimeframeThresholds.BearishMinTrendCountSum);

        var folds = new List<RegimeStabilityFold>();
        int foldIndex = 0;
        var windowStart = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        var globalEnd = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        while (true)
        {
            var windowEnd = windowStart.AddMonths(windowMonths);
            if (windowEnd > globalEnd) break;

            cancellationToken.ThrowIfCancellationRequested();

            var summary = await backtestService.RunBacktestAsync(
                windowStart,
                windowEnd,
                cancellationToken,
                persist: false,
                momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
                troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
                troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
                confluenceThresholdSet: confluenceThresholdSet,
                bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
                reEntryCooldownHours: -1,
                smaExitDebounceDays: 0,
                bullishRegimeBlockBuy: false,
                entryRequiresPriceAboveSma200: false,
                breakevenTriggerPercent: 0.0,
                trailingStopPercent: 0.0,
                trailingStopActivationPercent: 0.0,
                nearAthSellSuppressMinPercent: 0.0,
                athProximityBuyBlockMaxAthAgeDays: 0,
                macroOnlyMode: true);

            var hasTrades = summary.TotalSells > 0;
            var profit = summary.TotalProfitPercent;
            var bnh = summary.BuyAndHoldPercent;
            var delta = (profit.HasValue && bnh.HasValue) ? profit.Value - bnh.Value : (double?)null;
            var beatsBnH = delta.HasValue && delta.Value > 0;
            var isPositive = profit.HasValue && profit.Value > 0;

            string note;
            if (hasTrades)
            {
                note = delta.HasValue
                    ? $"{(beatsBnH ? "Schlaegt" : "Verfehlt")} BnH um {delta.Value:+0.0;-0.0}%"
                    : string.Empty;
            }
            else if (summary.TotalBuys > 0)
            {
                // Position eroeffnet, aber Hold laenger als Fensterlaenge - Position noch offen.
                note = $"Position offen (Buy vorhanden, Hold > {windowMonths}m Fenster), BnH {bnh:+0.0;-0.0;'n/a'}%";
            }
            else if (bnh.HasValue)
            {
                note = bnh.Value < 0
                    ? $"Kein Trade, BnH {bnh.Value:0.0}% (korrekt gemieden)"
                    : $"Kein Trade, BnH +{bnh.Value:0.0}% (verpasste Chance)";
            }
            else
            {
                note = "Kein Trade, keine BnH-Daten";
            }

            folds.Add(new RegimeStabilityFold
            {
                Fold = ++foldIndex,
                Start = windowStart,
                End = windowEnd,
                TotalBuys = summary.TotalBuys,
                TotalSells = summary.TotalSells,
                TotalProfitPercent = profit.HasValue ? Math.Round(profit.Value, 2) : null,
                BuyAndHoldPercent = bnh.HasValue ? Math.Round(bnh.Value, 2) : null,
                DeltaVsBuyAndHold = delta.HasValue ? Math.Round(delta.Value, 2) : null,
                BeatsBuyAndHold = beatsBnH,
                IsPositive = isPositive,
                Note = note,
            });

            windowStart = windowStart.AddMonths(stepMonths);
        }

        return BuildResult(startDate, endDate, windowMonths, stepMonths, folds);
    }

    private static RegimeStabilityResult BuildResult(
        DateTime fullStart,
        DateTime fullEnd,
        int windowMonths,
        int stepMonths,
        List<RegimeStabilityFold> folds)
    {
        var withTrades = folds.Where(f => f.TotalSells > 0).ToList();
        var withTradesPositive = withTrades.Count(f => f.IsPositive);
        var withTradesBeatBnH = withTrades.Count(f => f.BeatsBuyAndHold);
        var noTrades = folds.Where(f => f.TotalSells == 0).ToList();
        var openPositions = noTrades.Count(f => f.TotalBuys > 0);
        var pureNoTrade = noTrades.Where(f => f.TotalBuys == 0).ToList();
        var correctAvoidance = pureNoTrade.Count(f => f.BuyAndHoldPercent.HasValue && f.BuyAndHoldPercent.Value < 0);
        var missedOpportunity = pureNoTrade.Count(f => f.BuyAndHoldPercent.HasValue && f.BuyAndHoldPercent.Value >= 0);

        double? medianDelta = null;
        if (withTrades.Count > 0)
        {
            var deltas = withTrades
                .Where(f => f.DeltaVsBuyAndHold.HasValue)
                .Select(f => f.DeltaVsBuyAndHold!.Value)
                .OrderBy(v => v)
                .ToArray();
            if (deltas.Length > 0)
                medianDelta = Math.Round(Percentile(deltas, 50), 2);
        }

        var verdict = BuildVerdict(folds.Count, withTrades.Count, withTradesPositive, withTradesBeatBnH,
            openPositions, correctAvoidance, missedOpportunity, medianDelta);

        return new RegimeStabilityResult
        {
            FullStart = DateTime.SpecifyKind(fullStart, DateTimeKind.Utc),
            FullEnd = DateTime.SpecifyKind(fullEnd, DateTimeKind.Utc),
            WindowMonths = windowMonths,
            StepMonths = stepMonths,
            Folds = folds,
            WindowCount = folds.Count,
            WindowsWithTrades = withTrades.Count,
            WindowsWithTradesPositive = withTradesPositive,
            WindowsWithTradesBeatBnH = withTradesBeatBnH,
            WindowsNoTradesCorrectAvoidance = correctAvoidance,
            WindowsNoTradesMissedOpportunity = missedOpportunity,
            WindowsWithOpenPosition = openPositions,
            MedianDeltaVsBuyAndHold = medianDelta,
            Verdict = verdict,
        };
    }

    private static string BuildVerdict(
        int total,
        int withTrades,
        int positive,
        int beatBnH,
        int openPositions,
        int correctAvoidance,
        int missed,
        double? medianDelta)
    {
        if (total == 0) return "Zu wenige Fenster fuer eine Aussage.";

        var sb = new System.Text.StringBuilder();

        if (withTrades == 0)
        {
            sb.Append($"Kein Fenster ({total}) hatte abgeschlossene Trades. ");
            if (openPositions > 0)
                sb.Append($"{openPositions}/{total} Fenster: Einstieg erfolgt, aber Hold laenger als Fensterlaenge (erwartetes Verhalten bei langen Haltedauern). ");
            sb.Append($"Fenster ohne Einstieg: {correctAvoidance}x korrekt gemieden (BnH negativ), {missed}x Chance verpasst (BnH positiv, kein Buy).");
            return sb.ToString().Trim();
        }

        sb.Append($"Trades in {withTrades}/{total} Fenstern. ");
        sb.Append($"Positiv: {positive}/{withTrades}. ");
        sb.Append($"Schlaegt BnH: {beatBnH}/{withTrades}. ");

        if (openPositions > 0)
            sb.Append($"Offen (Hold > Fenster): {openPositions}. ");
        if (medianDelta.HasValue)
            sb.Append($"Median-Delta vs. BnH: {medianDelta.Value:+0.0;-0.0}%. ");
        if (correctAvoidance + missed > 0)
            sb.Append($"Ohne Einstieg: {correctAvoidance}x korrekt gemieden, {missed}x verpasst. ");

        if (withTrades >= 3 && positive == withTrades && beatBnH == withTrades)
            sb.Append("STARK: Konsistent positiv und BnH schlagend.");
        else if (withTrades >= 3 && (double)positive / withTrades >= 0.67)
            sb.Append("MODERAT: Mehrheitlich positiv.");
        else if (withTrades < 3)
            sb.Append($"UNZUREICHEND: Nur {withTrades} Fenster mit Trades - zu wenige fuer statistisch belastbare Aussage.");
        else
            sb.Append("SCHWACH: Inkonsistente Ergebnisse ueber Regime.");

        return sb.ToString().Trim();
    }

    private static double Percentile(double[] sortedAsc, double percent)
    {
        if (sortedAsc.Length == 0) return double.NaN;
        if (sortedAsc.Length == 1) return sortedAsc[0];
        var rank = percent / 100.0 * (sortedAsc.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sortedAsc[lo];
        return sortedAsc[lo] * (1 - (rank - lo)) + sortedAsc[hi] * (rank - lo);
    }
}
