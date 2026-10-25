using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

/// <summary>
/// Stationary-Block-Bootstrap (Politis &amp; Romano 1994) auf den taeglichen
/// Renditen der Strategie und von Buy-and-Hold.
///
/// Vorgehen:
/// 1. Echten Backtest mit Produktions-Defaults laufen lassen (identisch zu GET /backtest).
/// 2. Tageskerzen laden, In-Position-Maske aus Trades rekonstruieren.
/// 3. Tagesrenditen aus Tagesschluesskursen, Strategie-Tagesrendite = BnH-Rendite an In-Position-Tagen, sonst 0.
/// 4. Block-Bootstrap mit geometrisch verteilten Blocklaengen (Mittel L, Default 20 Handelstage)
///    erzeugt B synthetische Pfade.
/// 5. Pro Pfad werden Strategie-Total-Return, BnH-Total-Return und Alpha (Diff) compounded berechnet.
/// 6. Statistiken (CI) und p-Wert P(Alpha &lt;= 0) werden zurueckgegeben.
///
/// Wichtig: Der Bootstrap testet die statistische Robustheit *des realisierten Alphas* auf den
/// real beobachteten Tagesrenditen unter Erhalt der Autokorrelation - er sagt nichts ueber
/// Strategie-Verhalten auf nicht-gesehenen Marktregimes aus.
/// </summary>
public class StationaryBootstrapService(
    IBacktestService backtestService,
    IBinanceKlineService binanceKlineService) : IStationaryBootstrapService
{
    private const string DailyInterval = "1d";

    public async Task<BacktestBootstrapResult> RunAsync(
        DateTime startDate,
        DateTime endDate,
        int iterations,
        int meanBlockLength,
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
        if (meanBlockLength < 1)
        {
            throw new ArgumentException("meanBlockLength must be >= 1");
        }

        // 1. Echten Backtest mit Produktions-Defaults.
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

        // 2. Tageskerzen im Fenster laden.
        var startUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        var allDaily = await binanceKlineService.GetCandlesUpToAsync(
            DailyInterval, endDate, cancellationToken);

        var dailies = allDaily
            .Where(c => c.CloseTime >= startUtc && c.CloseTime <= endUtc && c.Close > 0)
            .OrderBy(c => c.CloseTime)
            .ToList();

        if (dailies.Count < 30)
        {
            throw new InvalidOperationException(
                $"Zu wenige Tageskerzen im Zeitraum ({dailies.Count}) - Bootstrap nicht durchfuehrbar.");
        }

        // 3. In-Position-Intervalle aus Trades rekonstruieren.
        //    Jeder abgeschlossene Trade hat EntryTimestamp + Timestamp (Exit).
        var positionIntervals = actualSummary.Trades
            .Where(t => t.EntryTimestamp.HasValue
                && t.EntryPrice.HasValue
                && t.ProfitPercent.HasValue)
            .Select(t => (
                Entry: DateTime.SpecifyKind(t.EntryTimestamp!.Value, DateTimeKind.Utc),
                Exit: DateTime.SpecifyKind(t.Timestamp, DateTimeKind.Utc)))
            .ToList();

        // 4. Tagesrenditen + In-Position-Maske.
        int n = dailies.Count - 1;  // n daily returns from n+1 closes
        var bnhReturns = new double[n];
        var strategyReturns = new double[n];
        int inMarketDays = 0;

        for (int i = 0; i < n; i++)
        {
            double prev = dailies[i].Close;
            double curr = dailies[i + 1].Close;
            double r = (curr - prev) / prev;
            bnhReturns[i] = r;

            // Strategie ist an Tag i+1 investiert, wenn ein Trade-Intervall dieses Datum abdeckt.
            var dayClose = dailies[i + 1].CloseTime;
            bool inPos = false;
            foreach (var (entry, exit) in positionIntervals)
            {
                if (dayClose > entry && dayClose <= exit)
                {
                    inPos = true;
                    break;
                }
            }
            strategyReturns[i] = inPos ? r : 0.0;
            if (inPos) inMarketDays++;
        }

        var actualStrategy = Compounded(strategyReturns);
        var actualBnH = Compounded(bnhReturns);
        var actualAlpha = actualStrategy - actualBnH;

        // 5. Stationary Block Bootstrap.
        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        double p = 1.0 / meanBlockLength;

        var strategySamples = new double[iterations];
        var bnhSamples = new double[iterations];
        var alphaSamples = new double[iterations];

        for (int it = 0; it < iterations; it++)
        {
            if ((it & 0x3FF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            double sStrat = 0;
            double sBnh = 0;
            int idx = rng.Next(0, n);

            for (int k = 0; k < n; k++)
            {
                // Logaddition fuer numerische Stabilitaet bei langen Pfaden.
                sStrat += Math.Log(1.0 + strategyReturns[idx]);
                sBnh += Math.Log(1.0 + bnhReturns[idx]);

                if (rng.NextDouble() < p)
                {
                    idx = rng.Next(0, n);
                }
                else
                {
                    idx = (idx + 1) % n;
                }
            }

            double stratPct = (Math.Exp(sStrat) - 1.0) * 100.0;
            double bnhPct = (Math.Exp(sBnh) - 1.0) * 100.0;
            strategySamples[it] = stratPct;
            bnhSamples[it] = bnhPct;
            alphaSamples[it] = stratPct - bnhPct;
        }

        // 6. Statistiken + p-Wert.
        var stratStats = BuildStats(strategySamples, actualStrategy);
        var bnhStats = BuildStats(bnhSamples, actualBnH);
        var alphaStats = BuildStats(alphaSamples, actualAlpha);

        int alphaNegOrZero = 0;
        for (int i = 0; i < alphaSamples.Length; i++)
        {
            if (alphaSamples[i] <= 0) alphaNegOrZero++;
        }
        double alphaP = (double)alphaNegOrZero / alphaSamples.Length;

        var result = new BacktestBootstrapResult
        {
            StartDate = startUtc,
            EndDate = endUtc,
            Iterations = iterations,
            TradingDays = n,
            MeanBlockLength = meanBlockLength,
            ActualStrategyCompoundedPercent = Math.Round(actualStrategy, 2),
            ActualBuyAndHoldCompoundedPercent = Math.Round(actualBnH, 2),
            ActualAlphaPercent = Math.Round(actualAlpha, 2),
            TimeInMarketPercent = Math.Round((double)inMarketDays / n * 100.0, 2),
            StrategyDistribution = stratStats,
            BuyAndHoldDistribution = bnhStats,
            AlphaDistribution = alphaStats,
            AlphaPValue = Math.Round(alphaP, 6),
        };

        result.Verdict = BuildVerdict(result);
        return result;
    }

    private static double Compounded(double[] returns)
    {
        double s = 0;
        for (int i = 0; i < returns.Length; i++)
        {
            s += Math.Log(1.0 + returns[i]);
        }
        return (Math.Exp(s) - 1.0) * 100.0;
    }

    private static NullDistributionStats BuildStats(double[] values, double actual)
    {
        if (values.Length == 0)
        {
            return new NullDistributionStats { PValueGreaterOrEqual = double.NaN };
        }

        var sorted = (double[])values.Clone();
        Array.Sort(sorted);

        double mean = 0;
        for (int i = 0; i < sorted.Length; i++) mean += sorted[i];
        mean /= sorted.Length;

        double variance = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            double d = sorted[i] - mean;
            variance += d * d;
        }
        variance /= sorted.Length;
        double std = Math.Sqrt(variance);

        int atLeastAsGood = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            if (sorted[i] >= actual) atLeastAsGood++;
        }
        double pValue = (double)atLeastAsGood / sorted.Length;

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

    private static string BuildVerdict(BacktestBootstrapResult r)
    {
        var p = r.AlphaPValue;
        var alpha = r.ActualAlphaPercent;
        var direction = alpha >= 0 ? "positiv" : "negativ";

        if (r.Iterations < 100)
        {
            return $"Zu wenige Iterationen ({r.Iterations}) fuer ein belastbares Urteil.";
        }

        string sig;
        if (p < 0.01) sig = "Sehr signifikant";
        else if (p < 0.05) sig = "Signifikant";
        else if (p < 0.20) sig = "Schwach signifikant";
        else if (p < 0.50) sig = "Nicht signifikant";
        else sig = "Strategie unterperformt typischerweise";

        return $"{sig} (p={p:F4}): realisiertes Alpha {alpha:+0.0;-0.0;0}% ({direction}) vs. BnH. " +
               $"Bootstrap-CI Alpha 5/95: [{r.AlphaDistribution.P05:+0.0;-0.0;0}%, {r.AlphaDistribution.P95:+0.0;-0.0;0}%]. " +
               $"Time-in-Market: {r.TimeInMarketPercent:F1}%, Blocklaenge L={r.MeanBlockLength}d, n={r.TradingDays} Tage.";
    }
}
