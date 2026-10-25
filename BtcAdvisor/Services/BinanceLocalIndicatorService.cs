using BtcAdvisor.Constants;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;

/// <summary>
/// Implements IBitcoinIndicatorService using locally computed indicators from Binance candles.
/// Replaces the DB-backed BitcoinIndicatorService for macro-timeframe experiments.
/// </summary>
public class BinanceLocalIndicatorService(
    IBinanceKlineService klineService,
    ILocalIndicatorComputationService indicatorComputation) : IBitcoinIndicatorService
{
    // How many candles to pass to indicator functions (covers EMA-200 warm-up + margin)
    private const int CandleWindow = 400;

    private static readonly string[] EmaPeriods = ["10", "20", "50", "100", "200"];

    public async Task<IndicatorEvaluationInput?> GetFormattedIndicatorData(
        DateTime dateTime,
        string timeSpan,
        string indicator,
        int? period,
        CancellationToken cancellationToken)
    {
        var interval = MapToInterval(timeSpan);
        var allCandles = await klineService.GetCandlesUpToAsync(interval, dateTime, cancellationToken);

        if (allCandles.Count < 2)
            return null;

        // Take at most CandleWindow candles ending at current
        var window = allCandles.Count <= CandleWindow
            ? allCandles
            : allCandles.Skip(allCandles.Count - CandleWindow).ToList();

        var n = window.Count;
        var opens  = window.Select(c => c.Open).ToArray();
        var highs  = window.Select(c => c.High).ToArray();
        var lows   = window.Select(c => c.Low).ToArray();
        var closes = window.Select(c => c.Close).ToArray();
        var currentDateTime = window[n - 1].CloseTime;

        return indicator switch
        {
            "price"          => BuildPrice(timeSpan, window, currentDateTime),
            "rsi"            => BuildRsi(timeSpan, closes, period ?? 14, currentDateTime),
            "sma"            => BuildSma(timeSpan, closes, period ?? 20, currentDateTime),
            "ema"            => BuildAllEma(timeSpan, closes, currentDateTime),
            "hma"            => BuildHma(timeSpan, closes, period ?? 9, currentDateTime),
            "ichimoku"       => BuildIchimoku(timeSpan, highs, lows, closes, currentDateTime),
            "stoch"          => BuildStoch(timeSpan, highs, lows, closes, currentDateTime),
            "stochf"         => BuildStochf(timeSpan, highs, lows, closes, currentDateTime),
            "cci"            => BuildCci(timeSpan, highs, lows, closes, currentDateTime),
            "adx"            => BuildAdx(timeSpan, highs, lows, closes, currentDateTime),
            "ao"             => BuildAo(timeSpan, highs, lows, currentDateTime),
            "mom"            => BuildMom(timeSpan, closes, period ?? 10, currentDateTime),
            "macd"           => BuildMacd(timeSpan, closes, currentDateTime),
            "willr"          => BuildWillR(timeSpan, highs, lows, closes, currentDateTime),
            "kickingbylength"=> BuildKicking(timeSpan, opens, highs, lows, closes, currentDateTime),
            _                => null
        };
    }

    // ── builder methods ────────────────────────────────────────────────────

    private static IndicatorEvaluationInput BuildPrice(string ts, IReadOnlyList<OhlcvCandle> window, DateTime dt)
    {
        var n = window.Count;
        var currentClose = window[n - 1].Close;
        var previousClose = window[n - 2].Close;
        var id = MakeId(ts, "price", null);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("price", new() { ["value"] = currentClose }) },
            Previous = { [id] = MakeDatum("price", new() { ["value"] = previousClose }) }
        };
    }

    private IndicatorEvaluationInput? BuildRsi(string ts, double[] closes, int period, DateTime dt)
    {
        var rsi = indicatorComputation.ComputeRsi(closes, period);
        var n = closes.Length;
        var cur = rsi[n - 1];
        var prev = rsi[n - 2];
        if (!cur.HasValue || !prev.HasValue) return null;

        var id = MakeId(ts, "rsi", period);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("rsi", new() { ["value"] = cur.Value }) },
            Previous = { [id] = MakeDatum("rsi", new() { ["value"] = prev.Value }) }
        };
    }

    private IndicatorEvaluationInput? BuildSma(string ts, double[] closes, int period, DateTime dt)
    {
        var sma = indicatorComputation.ComputeSma(closes, period);
        var n = closes.Length;
        var cur = sma[n - 1];
        var prev = sma[n - 2];
        if (!cur.HasValue || !prev.HasValue) return null;

        var id = MakeId(ts, "sma", period);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("sma", new() { ["value"] = cur.Value }) },
            Previous = { [id] = MakeDatum("sma", new() { ["value"] = prev.Value }) }
        };
    }

    /// <summary>
    /// For EMA evaluation, the EMAIndicator needs BOTH the requested period AND its short-term pair
    /// in the same Current/Previous dicts (see BitcoinConstants.EmaMapToShortTermIndicator).
    /// We compute all five EMA periods so any pair lookup succeeds.
    /// </summary>
    private IndicatorEvaluationInput? BuildAllEma(string ts, double[] closes, DateTime dt)
    {
        var n = closes.Length;
        var periods = new[] { 10, 20, 50, 100, 200 };
        var currentDict  = new Dictionary<string, IndicatorInputDatum>();
        var previousDict = new Dictionary<string, IndicatorInputDatum>();

        foreach (var p in periods)
        {
            var ema = indicatorComputation.ComputeEma(closes, p);
            var cur  = ema[n - 1];
            var prev = ema[n - 2];
            var id = MakeId(ts, "ema", p);
            currentDict[id]  = MakeDatum("ema", new() { ["value"] = cur  ?? double.NaN });
            previousDict[id] = MakeDatum("ema", new() { ["value"] = prev ?? double.NaN });
        }

        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = currentDict,
            Previous = previousDict
        };
    }

    private IndicatorEvaluationInput? BuildHma(string ts, double[] closes, int period, DateTime dt)
    {
        var hma = indicatorComputation.ComputeHma(closes, period);
        var n = closes.Length;
        var cur  = hma[n - 1];
        var prev = hma[n - 2];
        if (!cur.HasValue || !prev.HasValue) return null;

        var id = MakeId(ts, "hma", period);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("hma", new() { ["value"] = cur.Value }) },
            Previous = { [id] = MakeDatum("hma", new() { ["value"] = prev.Value }) }
        };
    }

    private IndicatorEvaluationInput? BuildIchimoku(string ts, double[] highs, double[] lows, double[] closes, DateTime dt)
    {
        const int tenkan = 9, kijun = 26, senkouB = 52, displacement = 26;
        var n = closes.Length;

        if (n < senkouB) return null;

        var ichi = indicatorComputation.ComputeIchimoku(highs, lows, closes, tenkan, kijun, senkouB, displacement);

        var current = ichi[n - 1];
        if (!current.TenkanSen.HasValue || !current.KijunSen.HasValue) return null;

        // currentSpanA/B: the cloud AT current bar (set 26 bars ahead of n-1-26 = n-27)
        var currentSpanA = current.SenkouSpanA;
        var currentSpanB = current.SenkouSpanB;

        if (!currentSpanA.HasValue || !currentSpanB.HasValue) return null;

        // spanA/B future: cloud 26 bars AHEAD of current — computed from current TenkanSen/KijunSen
        var futureSpanA = (current.TenkanSen.Value + current.KijunSen.Value) / 2.0;
        var futureSpanB = ComputeSenkouBFuture(highs, lows, n, senkouB);

        if (!futureSpanB.HasValue) return null;

        var id = MakeId(ts, "ichimoku", null);
        var curDatum = new IndicatorInputDatum { Indicator = "ichimoku" };
        curDatum.AddOrUpdateValue("conversion",   current.TenkanSen.Value);
        curDatum.AddOrUpdateValue("base",         current.KijunSen.Value);
        curDatum.AddOrUpdateValue("currentSpanA", currentSpanA.Value);
        curDatum.AddOrUpdateValue("currentSpanB", currentSpanB.Value);
        curDatum.AddOrUpdateValue("spanA",        futureSpanA);
        curDatum.AddOrUpdateValue("spanB",        futureSpanB.Value);
        curDatum.AddOrUpdateValue("laggingSpanA", double.NaN);
        curDatum.AddOrUpdateValue("laggingSpanB", double.NaN);

        var prev = ichi[n - 2];
        var prevDatum = new IndicatorInputDatum { Indicator = "ichimoku" };
        prevDatum.AddOrUpdateValue("conversion",   prev.TenkanSen   ?? double.NaN);
        prevDatum.AddOrUpdateValue("base",         prev.KijunSen    ?? double.NaN);
        prevDatum.AddOrUpdateValue("currentSpanA", prev.SenkouSpanA ?? double.NaN);
        prevDatum.AddOrUpdateValue("currentSpanB", prev.SenkouSpanB ?? double.NaN);
        prevDatum.AddOrUpdateValue("spanA",        double.NaN);
        prevDatum.AddOrUpdateValue("spanB",        double.NaN);
        prevDatum.AddOrUpdateValue("laggingSpanA", double.NaN);
        prevDatum.AddOrUpdateValue("laggingSpanB", double.NaN);

        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = curDatum },
            Previous = { [id] = prevDatum }
        };
    }

    private IndicatorEvaluationInput? BuildStoch(string ts, double[] highs, double[] lows, double[] closes, DateTime dt)
    {
        var stoch = indicatorComputation.ComputeStoch(highs, lows, closes);
        var n = closes.Length;
        var cur  = stoch[n - 1];
        var prev = stoch[n - 2];
        if (!cur.K.HasValue || !cur.D.HasValue) return null;
        if (!prev.K.HasValue || !prev.D.HasValue) return null;

        var id = MakeId(ts, "stoch", null);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("stoch", new() { ["valueK"] = cur.K.Value, ["valueD"] = cur.D.Value }) },
            Previous = { [id] = MakeDatum("stoch", new() { ["valueK"] = prev.K.Value, ["valueD"] = prev.D.Value }) }
        };
    }

    private IndicatorEvaluationInput? BuildStochf(string ts, double[] highs, double[] lows, double[] closes, DateTime dt)
    {
        var stochf = indicatorComputation.ComputeStochf(highs, lows, closes);
        var n = closes.Length;
        var cur  = stochf[n - 1];
        var prev = stochf[n - 2];
        if (!cur.K.HasValue || !cur.D.HasValue) return null;
        if (!prev.K.HasValue || !prev.D.HasValue) return null;

        var id = MakeId(ts, "stochf", null);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("stochf", new() { ["valueFastK"] = cur.K.Value, ["valueFastD"] = cur.D.Value }) },
            Previous = { [id] = MakeDatum("stochf", new() { ["valueFastK"] = prev.K.Value, ["valueFastD"] = prev.D.Value }) }
        };
    }

    private IndicatorEvaluationInput? BuildCci(string ts, double[] highs, double[] lows, double[] closes, DateTime dt)
    {
        var cci = indicatorComputation.ComputeCci(highs, lows, closes);
        var n = closes.Length;
        var cur  = cci[n - 1];
        var prev = cci[n - 2];
        if (!cur.HasValue || !prev.HasValue) return null;

        var id = MakeId(ts, "cci", null);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("cci", new() { ["value"] = cur.Value }) },
            Previous = { [id] = MakeDatum("cci", new() { ["value"] = prev.Value }) }
        };
    }

    private IndicatorEvaluationInput? BuildAdx(string ts, double[] highs, double[] lows, double[] closes, DateTime dt)
    {
        var adx = indicatorComputation.ComputeAdx(highs, lows, closes);
        var n = closes.Length;
        var cur  = adx[n - 1];
        var prev = adx[n - 2];
        if (!cur.HasValue || !prev.HasValue) return null;

        var id = MakeId(ts, "adx", null);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("adx", new() { ["value"] = cur.Value }) },
            Previous = { [id] = MakeDatum("adx", new() { ["value"] = prev.Value }) }
        };
    }

    private IndicatorEvaluationInput? BuildAo(string ts, double[] highs, double[] lows, DateTime dt)
    {
        var ao = indicatorComputation.ComputeAo(highs, lows);
        var n = highs.Length;
        var cur  = ao[n - 1];
        var prev = ao[n - 2];
        if (!cur.HasValue || !prev.HasValue) return null;

        var id = MakeId(ts, "ao", null);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("ao", new() { ["value"] = cur.Value }) },
            Previous = { [id] = MakeDatum("ao", new() { ["value"] = prev.Value }) }
        };
    }

    private IndicatorEvaluationInput? BuildMom(string ts, double[] closes, int period, DateTime dt)
    {
        var mom = indicatorComputation.ComputeMom(closes, period);
        var n = closes.Length;
        var cur  = mom[n - 1];
        var prev = mom[n - 2];
        if (!cur.HasValue || !prev.HasValue) return null;

        var id = MakeId(ts, "mom", period);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("mom", new() { ["value"] = cur.Value }) },
            Previous = { [id] = MakeDatum("mom", new() { ["value"] = prev.Value }) }
        };
    }

    private IndicatorEvaluationInput? BuildMacd(string ts, double[] closes, DateTime dt)
    {
        var macd = indicatorComputation.ComputeMacd(closes);
        var n = closes.Length;
        var cur  = macd[n - 1];
        var prev = macd[n - 2];
        if (!cur.Macd.HasValue || !cur.Signal.HasValue || !cur.Histogram.HasValue) return null;
        if (!prev.Macd.HasValue || !prev.Signal.HasValue || !prev.Histogram.HasValue) return null;

        var id = MakeId(ts, "macd", null);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("macd", new() {
                ["valueMacd"]      = cur.Macd.Value,
                ["valueMacdsignal"]= cur.Signal.Value,
                ["valueMacdhist"]  = cur.Histogram.Value }) },
            Previous = { [id] = MakeDatum("macd", new() {
                ["valueMacd"]      = prev.Macd.Value,
                ["valueMacdsignal"]= prev.Signal.Value,
                ["valueMacdhist"]  = prev.Histogram.Value }) }
        };
    }

    private IndicatorEvaluationInput? BuildWillR(string ts, double[] highs, double[] lows, double[] closes, DateTime dt)
    {
        var willr = indicatorComputation.ComputeWillR(highs, lows, closes);
        var n = closes.Length;
        var cur  = willr[n - 1];
        var prev = willr[n - 2];
        if (!cur.HasValue || !prev.HasValue) return null;

        var id = MakeId(ts, "willr", null);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("willr", new() { ["value"] = cur.Value }) },
            Previous = { [id] = MakeDatum("willr", new() { ["value"] = prev.Value }) }
        };
    }

    private IndicatorEvaluationInput? BuildKicking(string ts, double[] opens, double[] highs, double[] lows, double[] closes, DateTime dt)
    {
        var kicking = indicatorComputation.ComputeKickingByLength(opens, highs, lows, closes);
        var n = closes.Length;
        var cur  = kicking[n - 1];
        var prev = kicking[n - 2];

        var id = MakeId(ts, "kickingbylength", null);
        return new IndicatorEvaluationInput
        {
            DateTime = dt,
            Current  = { [id] = MakeDatum("kickingbylength", new() { ["value"] = (double)cur }) },
            Previous = { [id] = MakeDatum("kickingbylength", new() { ["value"] = (double)prev }) }
        };
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static string MapToInterval(string timeSpan) => timeSpan switch
    {
        "5m"  => "5m",
        "15m" => "15m",
        "30m" => "30m",
        "1h"  => "1h",
        "2h"  => "2h",
        "4h"  => "4h",
        "12h" => "12h",
        "1d"  => "1d",
        "1w"  => "1w",
        _     => throw new ArgumentException($"Unsupported Binance timeSpan: {timeSpan}", nameof(timeSpan))
    };

    private static string MakeId(string timeSpan, string indicator, int? period)
        => $"binance_BTC/USDT_{timeSpan}_{indicator}_{period?.ToString() ?? "0"}_0";

    private static IndicatorInputDatum MakeDatum(string indicator, Dictionary<string, double> values)
    {
        var datum = new IndicatorInputDatum { Indicator = indicator };
        foreach (var kv in values)
            datum.AddOrUpdateValue(kv.Key, kv.Value);
        return datum;
    }

    /// <summary>
    /// Computes the future Senkou Span B: midpoint of highest high and lowest low
    /// over the last <paramref name="senkouBPeriod"/> candles.
    /// </summary>
    private static double? ComputeSenkouBFuture(double[] highs, double[] lows, int n, int senkouBPeriod)
    {
        if (n < senkouBPeriod) return null;
        var start = n - senkouBPeriod;
        var highest = highs.Skip(start).Take(senkouBPeriod).Max();
        var lowest  = lows.Skip(start).Take(senkouBPeriod).Min();
        return (highest + lowest) / 2.0;
    }
}
