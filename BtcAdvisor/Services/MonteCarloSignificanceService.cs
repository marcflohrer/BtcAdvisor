using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

/// <summary>
/// Statistische Signifikanzanalyse fuer Backtest-Deals via Random-Entry Monte Carlo.
/// Beantwortet die Frage: "Ist das Ergebnis ein Zufallstreffer?"
///
/// Wichtig: Der reale Backtest wird mit den **identischen Produktions-Defaults** ausgefuehrt,
/// die auch GET /backtest verwendet (macroOnlyMode=true, vollstaendige Confluence-Thresholds).
/// Andernfalls testet der MC die falsche Strategie und der p-Wert ist wertlos.
/// </summary>
public class MonteCarloSignificanceService(
    IBacktestService backtestService,
    IBinanceKlineService binanceKlineService) : IMonteCarloSignificanceService
{
    private const string CandleInterval = "2h";

    public async Task<BacktestMonteCarloResult> RunAsync(
        DateTime startDate,
        DateTime endDate,
        int iterations,
        int? seed,
        CancellationToken cancellationToken)
    {
        if (endDate <= startDate)
        {
            throw new ArgumentException("endDate must be after startDate");
        }
        if (iterations <= 0)
        {
            throw new ArgumentException("iterations must be positive");
        }

        // 1. Echten Backtest mit PRODUKTIONS-DEFAULTS durchlaufen
        //    (identisch zu GET /backtest ohne Override-Parameter).
        var confluenceThresholdSet = new LongTimeframeConfluenceThresholdSet(
            bullishMinAlignedRanges: LongTimeframeThresholds.BullishMinAlignedRanges,
            bullishMaxAlignedRanges: int.MaxValue,
            bullishMinAlignedAnchorRanges: LongTimeframeThresholds.BullishMinAlignedAnchorRanges,
            bullishMinTrendCountSum: LongTimeframeThresholds.BullishMinTrendCountSum,
            bullishMaxTrendCountSum: int.MaxValue,
            bearishMinAlignedRanges: LongTimeframeThresholds.BearishMinAlignedRanges,
            bearishMinTrendCountSum: LongTimeframeThresholds.BearishMinTrendCountSum);

        var actualSummary = await backtestService.RunBacktestAsync(
            startDate,
            endDate,
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

        var actualDeals = actualSummary.Trades
            .Where(t => t.EntryTimestamp.HasValue
                && t.EntryPrice.HasValue
                && t.EntryPrice.Value > 0m
                && t.ProfitPercent.HasValue)
            .ToList();

        if (actualDeals.Count == 0)
        {
            throw new InvalidOperationException(
                "Keine abgeschlossenen Deals im Zeitraum - Monte-Carlo-Test nicht durchfuehrbar.");
        }

        var holdHoursList = actualDeals
            .Select(t => t.HoldHours
                ?? (t.Timestamp - t.EntryTimestamp!.Value).TotalHours)
            .ToList();

        // 2. Preis-Kerzen laden (2h-Grid).
        var allCandles = await binanceKlineService.GetCandlesUpToAsync(
            CandleInterval, endDate, cancellationToken);

        var startUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        var prices = allCandles
            .Where(c => c.CloseTime >= startUtc && c.CloseTime <= endUtc && c.Close > 0)
            .OrderBy(c => c.CloseTime)
            .Select(c => (Time: c.CloseTime, Price: c.Close))
            .ToList();

        if (prices.Count < 10)
        {
            throw new InvalidOperationException(
                $"Zu wenige Kerzen im Zeitraum ({prices.Count}) - Monte-Carlo-Test nicht durchfuehrbar.");
        }

        var times = prices.Select(p => p.Time).ToArray();
        var priceValues = prices.Select(p => p.Price).ToArray();

        // 3. Reale Kennzahlen berechnen.
        var actualTotal = actualDeals.Sum(t => t.ProfitPercent!.Value);
        var actualMean = actualTotal / actualDeals.Count;
        var actualWinRate = (double)actualDeals.Count(t => t.ProfitPercent!.Value > 0)
            / actualDeals.Count * 100.0;
        var actualCompoundedEquity = actualDeals.Aggregate(
            1.0, (acc, t) => acc * (1.0 + t.ProfitPercent!.Value / 100.0));
        var actualCompounded = (actualCompoundedEquity - 1.0) * 100.0;

        // 4. Monte Carlo.
        var rng = seed.HasValue ? new Random(seed.Value) : new Random();

        var totalReturns = new List<double>(iterations);
        var compoundedReturns = new List<double>(iterations);
        var winRates = new List<double>(iterations);
        var meanReturns = new List<double>(iterations);

        // Sicherheitsbegrenzung: wenn die laengste Haltedauer das Restfenster sprengt,
        // sind valide Iterationen unmoeglich. In diesem Fall fruehzeitig abbrechen.
        var longestHoldHours = holdHoursList.Max();
        var windowHours = (endUtc - startUtc).TotalHours;
        if (longestHoldHours >= windowHours)
        {
            throw new InvalidOperationException(
                $"Laengste Haltedauer ({longestHoldHours:F1}h) >= Backtest-Fenster ({windowHours:F1}h).");
        }

        for (int i = 0; i < iterations; i++)
        {
            if ((i & 0x3FF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double iterTotal = 0;
            double iterEquity = 1.0;
            int iterWins = 0;
            bool ok = true;

            foreach (var holdHours in holdHoursList)
            {
                int entryIdx = rng.Next(0, prices.Count - 1);
                var entryTime = times[entryIdx];
                var entryPrice = priceValues[entryIdx];
                var exitTime = entryTime.AddHours(holdHours);

                if (exitTime > endUtc)
                {
                    ok = false;
                    break;
                }

                int idx = Array.BinarySearch(times, exitTime);
                if (idx < 0) idx = ~idx;
                if (idx >= prices.Count)
                {
                    ok = false;
                    break;
                }

                var exitPrice = priceValues[idx];
                if (entryPrice <= 0) { ok = false; break; }

                var profitPct = (exitPrice - entryPrice) / entryPrice * 100.0;
                iterTotal += profitPct;
                iterEquity *= (1.0 + profitPct / 100.0);
                if (profitPct > 0) iterWins++;
            }

            if (!ok) continue;

            totalReturns.Add(iterTotal);
            compoundedReturns.Add((iterEquity - 1.0) * 100.0);
            winRates.Add((double)iterWins / holdHoursList.Count * 100.0);
            meanReturns.Add(iterTotal / holdHoursList.Count);
        }

        var result = new BacktestMonteCarloResult
        {
            StartDate = startUtc,
            EndDate = endUtc,
            Iterations = iterations,
            ValidIterations = totalReturns.Count,
            ActualTradeCount = actualDeals.Count,
            ActualTotalReturnPercent = Math.Round(actualTotal, 2),
            ActualCompoundedReturnPercent = Math.Round(actualCompounded, 2),
            ActualWinRatePercent = Math.Round(actualWinRate, 2),
            ActualMeanReturnPercent = Math.Round(actualMean, 2),
            ActualHoldingHours = holdHoursList.Select(h => Math.Round(h, 1)).ToList(),
            TotalReturn = BuildStats(totalReturns, actualTotal),
            CompoundedReturn = BuildStats(compoundedReturns, actualCompounded),
            WinRate = BuildStats(winRates, actualWinRate),
            MeanReturn = BuildStats(meanReturns, actualMean),
        };

        result.Verdict = BuildVerdict(result);
        return result;
    }

    private static NullDistributionStats BuildStats(List<double> values, double actual)
    {
        if (values.Count == 0)
        {
            return new NullDistributionStats { PValueGreaterOrEqual = double.NaN };
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var mean = sorted.Average();
        var variance = sorted.Sum(v => (v - mean) * (v - mean)) / sorted.Length;
        var std = Math.Sqrt(variance);

        var atLeastAsGood = sorted.Count(v => v >= actual);
        var pValue = (double)atLeastAsGood / sorted.Length;

        return new NullDistributionStats
        {
            Mean = Math.Round(mean, 4),
            Median = Math.Round(Percentile(sorted, 50), 4),
            StdDev = Math.Round(std, 4),
            Min = Math.Round(sorted[0], 4),
            Max = Math.Round(sorted[^1], 4),
            P05 = Math.Round(Percentile(sorted, 5), 4),
            P25 = Math.Round(Percentile(sorted, 25), 4),
            P75 = Math.Round(Percentile(sorted, 75), 4),
            P95 = Math.Round(Percentile(sorted, 95), 4),
            PValueGreaterOrEqual = Math.Round(pValue, 6),
            Histogram = BuildHistogram(sorted, 20),
        };
    }

    private static double Percentile(double[] sortedAsc, double percent)
    {
        if (sortedAsc.Length == 0) return double.NaN;
        if (sortedAsc.Length == 1) return sortedAsc[0];
        var rank = percent / 100.0 * (sortedAsc.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sortedAsc[lo];
        var weight = rank - lo;
        return sortedAsc[lo] * (1 - weight) + sortedAsc[hi] * weight;
    }

    private static List<HistogramBucket> BuildHistogram(double[] sortedAsc, int bucketCount)
    {
        var buckets = new List<HistogramBucket>(bucketCount);
        if (sortedAsc.Length == 0) return buckets;
        var min = sortedAsc[0];
        var max = sortedAsc[^1];
        if (Math.Abs(max - min) < 1e-12)
        {
            buckets.Add(new HistogramBucket(min, min, sortedAsc.Length));
            return buckets;
        }
        var width = (max - min) / bucketCount;
        var counts = new int[bucketCount];
        foreach (var v in sortedAsc)
        {
            var b = (int)((v - min) / width);
            if (b >= bucketCount) b = bucketCount - 1;
            counts[b]++;
        }
        for (int i = 0; i < bucketCount; i++)
        {
            buckets.Add(new HistogramBucket(
                Math.Round(min + i * width, 4),
                Math.Round(min + (i + 1) * width, 4),
                counts[i]));
        }
        return buckets;
    }

    private static string BuildVerdict(BacktestMonteCarloResult r)
    {
        var p = r.TotalReturn.PValueGreaterOrEqual;
        if (r.ValidIterations < 100)
        {
            return $"Zu wenige valide Iterationen ({r.ValidIterations}) fuer ein belastbares Urteil.";
        }
        if (p < 0.01)
        {
            return $"Sehr signifikant (p={p:F4}): Strategie deutlich besser als Zufall (n={r.ActualTradeCount}).";
        }
        if (p < 0.05)
        {
            return $"Signifikant (p={p:F4}): Strategie wahrscheinlich kein Zufallstreffer (n={r.ActualTradeCount}).";
        }
        if (p < 0.20)
        {
            return $"Schwach signifikant (p={p:F4}): Hinweis auf Edge, aber n={r.ActualTradeCount} zu klein fuer harte Aussage.";
        }
        if (p < 0.50)
        {
            return $"Nicht signifikant (p={p:F4}): Ergebnis im Rahmen zufaelliger Streuung (n={r.ActualTradeCount}).";
        }
        return $"Wahrscheinlich Zufallstreffer (p={p:F4}): Zufallsstrategien sind im Schnitt besser (n={r.ActualTradeCount}).";
    }
}
