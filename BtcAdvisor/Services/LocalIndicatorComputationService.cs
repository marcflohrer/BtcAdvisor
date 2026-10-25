namespace BtcAdvisor.Services;

public class LocalIndicatorComputationService : ILocalIndicatorComputationService
{
    public IReadOnlyList<double?> ComputeSma(IReadOnlyList<double> closes, int period)
    {
        ValidateInputs(closes, period);

        var result = Enumerable.Repeat<double?>(null, closes.Count).ToArray();
        double runningSum = 0d;

        for (int i = 0; i < closes.Count; i++)
        {
            runningSum += closes[i];
            if (i >= period)
            {
                runningSum -= closes[i - period];
            }

            if (i >= period - 1)
            {
                result[i] = runningSum / period;
            }
        }

        return result;
    }

    public IReadOnlyList<double?> ComputeEma(IReadOnlyList<double> closes, int period)
    {
        ValidateInputs(closes, period);

        var result = Enumerable.Repeat<double?>(null, closes.Count).ToArray();
        if (closes.Count < period)
        {
            return result;
        }

        var alpha = 2d / (period + 1d);

        var seed = closes.Take(period).Average();
        result[period - 1] = seed;

        for (int i = period; i < closes.Count; i++)
        {
            var previous = result[i - 1]!.Value;
            result[i] = (closes[i] - previous) * alpha + previous;
        }

        return result;
    }

    public IReadOnlyList<double?> ComputeRsi(IReadOnlyList<double> closes, int period)
    {
        ValidateInputs(closes, period);

        var result = Enumerable.Repeat<double?>(null, closes.Count).ToArray();
        if (closes.Count <= period)
        {
            return result;
        }

        double gainSum = 0d;
        double lossSum = 0d;

        for (int i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0)
            {
                gainSum += change;
            }
            else
            {
                lossSum += -change;
            }
        }

        var avgGain = gainSum / period;
        var avgLoss = lossSum / period;
        result[period] = ToRsi(avgGain, avgLoss);

        for (int i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = Math.Max(change, 0d);
            var loss = Math.Max(-change, 0d);

            avgGain = ((avgGain * (period - 1)) + gain) / period;
            avgLoss = ((avgLoss * (period - 1)) + loss) / period;
            result[i] = ToRsi(avgGain, avgLoss);
        }

        return result;
    }

    public IReadOnlyList<double?> ComputeHma(IReadOnlyList<double> closes, int period)
    {
        ValidateInputs(closes, period);

        var halfPeriod = Math.Max(period / 2, 1);
        var sqrtPeriod = Math.Max((int)Math.Sqrt(period), 1);

        var wmaHalf = ComputeWma(closes, halfPeriod);
        var wmaFull = ComputeWma(closes, period);

        var transformed = Enumerable.Repeat<double?>(null, closes.Count).ToArray();
        for (int i = 0; i < closes.Count; i++)
        {
            if (wmaHalf[i].HasValue && wmaFull[i].HasValue)
            {
                transformed[i] = (2d * wmaHalf[i]!.Value) - wmaFull[i]!.Value;
            }
        }

        return ComputeWma(transformed, sqrtPeriod);
    }

    public IReadOnlyList<IchimokuPoint> ComputeIchimoku(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int tenkanPeriod = 9,
        int kijunPeriod = 26,
        int senkouBPeriod = 52,
        int displacement = 26)
    {
        ValidateOhlc(highs, lows, closes);
        ValidatePositivePeriods(tenkanPeriod, kijunPeriod, senkouBPeriod, displacement);

        var count = closes.Count;
        var result = Enumerable.Range(0, count)
            .Select(_ => new IchimokuPoint(null, null, null, null, null))
            .ToArray();

        var tenkan = MidpointOfRange(highs, lows, tenkanPeriod);
        var kijun = MidpointOfRange(highs, lows, kijunPeriod);
        var senkouBBase = MidpointOfRange(highs, lows, senkouBPeriod);

        for (int i = 0; i < count; i++)
        {
            var tenkanValue = tenkan[i];
            var kijunValue = kijun[i];
            var senkouAIndex = i + displacement;
            var senkouBIndex = i + displacement;
            var chikouIndex = i - displacement;

            var point = result[i];
            result[i] = point with
            {
                TenkanSen = tenkanValue,
                KijunSen = kijunValue
            };

            if (senkouAIndex < count && tenkanValue.HasValue && kijunValue.HasValue)
            {
                result[senkouAIndex] = result[senkouAIndex] with
                {
                    SenkouSpanA = (tenkanValue.Value + kijunValue.Value) / 2d
                };
            }

            if (senkouBIndex < count && senkouBBase[i].HasValue)
            {
                result[senkouBIndex] = result[senkouBIndex] with
                {
                    SenkouSpanB = senkouBBase[i]
                };
            }

            if (chikouIndex >= 0)
            {
                result[chikouIndex] = result[chikouIndex] with
                {
                    ChikouSpan = closes[i]
                };
            }
        }

        return result;
    }

    public IReadOnlyList<StochasticPoint> ComputeStoch(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int kPeriod = 14,
        int dPeriod = 3)
    {
        ValidateOhlc(highs, lows, closes);
        ValidatePositivePeriods(kPeriod, dPeriod);

        var kValues = Enumerable.Repeat<double?>(null, closes.Count).ToArray();
        for (int i = kPeriod - 1; i < closes.Count; i++)
        {
            var highest = Max(highs, i - kPeriod + 1, i);
            var lowest = Min(lows, i - kPeriod + 1, i);
            var range = highest - lowest;
            kValues[i] = range == 0d ? 0d : ((closes[i] - lowest) / range) * 100d;
        }

        var dValues = ComputeSmaOnNullable(kValues, dPeriod);
        return Enumerable.Range(0, closes.Count)
            .Select(i => new StochasticPoint(kValues[i], dValues[i]))
            .ToArray();
    }

    public IReadOnlyList<double?> ComputeCci(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int period = 20)
    {
        ValidateOhlc(highs, lows, closes);
        ValidatePositivePeriods(period);

        var tp = Enumerable.Range(0, closes.Count)
            .Select(i => (highs[i] + lows[i] + closes[i]) / 3d)
            .ToArray();
        var smaTp = ComputeSma(tp, period);
        var cci = Enumerable.Repeat<double?>(null, closes.Count).ToArray();

        for (int i = period - 1; i < closes.Count; i++)
        {
            var mean = smaTp[i]!.Value;
            double mdSum = 0d;
            for (int j = i - period + 1; j <= i; j++)
            {
                mdSum += Math.Abs(tp[j] - mean);
            }

            var meanDeviation = mdSum / period;
            cci[i] = meanDeviation == 0d ? 0d : (tp[i] - mean) / (0.015d * meanDeviation);
        }

        return cci;
    }

    public IReadOnlyList<double?> ComputeAdx(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int period = 14)
    {
        ValidateOhlc(highs, lows, closes);
        ValidatePositivePeriods(period);

        var count = closes.Count;
        var adx = Enumerable.Repeat<double?>(null, count).ToArray();
        if (count <= period)
        {
            return adx;
        }

        var tr = new double[count];
        var plusDm = new double[count];
        var minusDm = new double[count];
        var dx = Enumerable.Repeat<double?>(null, count).ToArray();

        for (int i = 1; i < count; i++)
        {
            var highDiff = highs[i] - highs[i - 1];
            var lowDiff = lows[i - 1] - lows[i];

            plusDm[i] = (highDiff > lowDiff && highDiff > 0d) ? highDiff : 0d;
            minusDm[i] = (lowDiff > highDiff && lowDiff > 0d) ? lowDiff : 0d;

            var tr1 = highs[i] - lows[i];
            var tr2 = Math.Abs(highs[i] - closes[i - 1]);
            var tr3 = Math.Abs(lows[i] - closes[i - 1]);
            tr[i] = Math.Max(tr1, Math.Max(tr2, tr3));
        }

        double trSmooth = 0d;
        double plusSmooth = 0d;
        double minusSmooth = 0d;
        for (int i = 1; i <= period; i++)
        {
            trSmooth += tr[i];
            plusSmooth += plusDm[i];
            minusSmooth += minusDm[i];
        }

        for (int i = period; i < count; i++)
        {
            if (i > period)
            {
                trSmooth = trSmooth - (trSmooth / period) + tr[i];
                plusSmooth = plusSmooth - (plusSmooth / period) + plusDm[i];
                minusSmooth = minusSmooth - (minusSmooth / period) + minusDm[i];
            }

            if (trSmooth == 0d)
            {
                dx[i] = 0d;
                continue;
            }

            var plusDi = 100d * (plusSmooth / trSmooth);
            var minusDi = 100d * (minusSmooth / trSmooth);
            var sum = plusDi + minusDi;
            dx[i] = sum == 0d ? 0d : 100d * (Math.Abs(plusDi - minusDi) / sum);
        }

        var firstAdxIndex = (period * 2) - 1;
        if (firstAdxIndex >= count)
        {
            return adx;
        }

        var initialValues = dx.Skip(period).Take(period).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        if (initialValues.Length == period)
        {
            adx[firstAdxIndex] = initialValues.Average();
        }

        for (int i = firstAdxIndex + 1; i < count; i++)
        {
            if (!dx[i].HasValue || !adx[i - 1].HasValue)
            {
                continue;
            }

            adx[i] = ((adx[i - 1]!.Value * (period - 1)) + dx[i]!.Value) / period;
        }

        return adx;
    }

    public IReadOnlyList<double?> ComputeAtr(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int period = 14)
    {
        ValidateOhlc(highs, lows, closes);
        ValidatePositivePeriods(period);

        var count = closes.Count;
        var atr = Enumerable.Repeat<double?>(null, count).ToArray();
        if (count <= period)
        {
            return atr;
        }

        var trueRanges = new double[count];
        for (int i = 1; i < count; i++)
        {
            var hl = highs[i] - lows[i];
            var hc = Math.Abs(highs[i] - closes[i - 1]);
            var lc = Math.Abs(lows[i] - closes[i - 1]);
            trueRanges[i] = Math.Max(hl, Math.Max(hc, lc));
        }

        // Initial ATR = simple mean of TRs[1..period]
        double initial = 0d;
        for (int i = 1; i <= period; i++)
        {
            initial += trueRanges[i];
        }
        atr[period] = initial / period;

        // Wilder smoothing
        for (int i = period + 1; i < count; i++)
        {
            atr[i] = ((atr[i - 1]!.Value * (period - 1)) + trueRanges[i]) / period;
        }

        return atr;
    }

    public IReadOnlyList<BollingerPoint> ComputeBollinger(
        IReadOnlyList<double> closes,
        int period = 20,
        double stdDevMultiplier = 2.0)
    {
        ArgumentNullException.ThrowIfNull(closes);
        ValidatePositivePeriods(period);
        if (stdDevMultiplier <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(stdDevMultiplier), "stdDevMultiplier must be positive");
        }

        var n = closes.Count;
        var result = new BollingerPoint[n];
        for (int i = 0; i < n; i++)
        {
            if (i < period - 1)
            {
                result[i] = new BollingerPoint(null, null, null, null);
                continue;
            }

            double sum = 0d;
            for (int j = i - period + 1; j <= i; j++)
            {
                sum += closes[j];
            }
            var mean = sum / period;

            double sqSum = 0d;
            for (int j = i - period + 1; j <= i; j++)
            {
                var diff = closes[j] - mean;
                sqSum += diff * diff;
            }
            var stdDev = Math.Sqrt(sqSum / period);

            var upper = mean + (stdDevMultiplier * stdDev);
            var lower = mean - (stdDevMultiplier * stdDev);
            var widthPercent = mean == 0d ? (double?)null : ((upper - lower) / mean) * 100d;

            result[i] = new BollingerPoint(upper, mean, lower, widthPercent);
        }

        return result;
    }

    public IReadOnlyList<double> ComputeObv(IReadOnlyList<double> closes, IReadOnlyList<double> volumes)
    {
        ArgumentNullException.ThrowIfNull(closes);
        ArgumentNullException.ThrowIfNull(volumes);
        if (closes.Count != volumes.Count)
        {
            throw new ArgumentException("closes and volumes must have the same length");
        }

        var n = closes.Count;
        var obv = new double[n];
        if (n == 0)
        {
            return obv;
        }
        obv[0] = 0d;
        for (int i = 1; i < n; i++)
        {
            if (closes[i] > closes[i - 1])
            {
                obv[i] = obv[i - 1] + volumes[i];
            }
            else if (closes[i] < closes[i - 1])
            {
                obv[i] = obv[i - 1] - volumes[i];
            }
            else
            {
                obv[i] = obv[i - 1];
            }
        }

        return obv;
    }

    public IReadOnlyList<double?> ComputeVwap(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        IReadOnlyList<double> volumes,
        int period = 20)
    {
        ArgumentNullException.ThrowIfNull(highs);
        ArgumentNullException.ThrowIfNull(lows);
        ArgumentNullException.ThrowIfNull(closes);
        ArgumentNullException.ThrowIfNull(volumes);
        if (highs.Count != lows.Count || highs.Count != closes.Count || highs.Count != volumes.Count)
        {
            throw new ArgumentException("highs, lows, closes and volumes must have the same length");
        }
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "period must be positive");
        }

        var n = closes.Count;
        var result = new double?[n];

        // typical price * volume and rolling sums
        double pvSum = 0d;
        double vSum = 0d;
        for (int i = 0; i < n; i++)
        {
            var tp = (highs[i] + lows[i] + closes[i]) / 3d;
            pvSum += tp * volumes[i];
            vSum += volumes[i];

            if (i >= period)
            {
                var oldTp = (highs[i - period] + lows[i - period] + closes[i - period]) / 3d;
                pvSum -= oldTp * volumes[i - period];
                vSum -= volumes[i - period];
            }

            if (i >= period - 1)
            {
                result[i] = vSum > 0d ? pvSum / vSum : null;
            }
            else
            {
                result[i] = null;
            }
        }

        return result;
    }

    public IReadOnlyList<double?> ComputeMfi(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        IReadOnlyList<double> volumes,
        int period = 14)
    {
        ArgumentNullException.ThrowIfNull(highs);
        ArgumentNullException.ThrowIfNull(lows);
        ArgumentNullException.ThrowIfNull(closes);
        ArgumentNullException.ThrowIfNull(volumes);
        if (highs.Count != lows.Count || highs.Count != closes.Count || highs.Count != volumes.Count)
        {
            throw new ArgumentException("highs, lows, closes and volumes must have the same length");
        }
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "period must be positive");
        }

        var n = closes.Count;
        var result = new double?[n];
        if (n == 0)
        {
            return result;
        }

        var typicals = new double[n];
        var moneyFlow = new double[n];
        var positive = new double[n];
        var negative = new double[n];

        typicals[0] = (highs[0] + lows[0] + closes[0]) / 3d;
        moneyFlow[0] = typicals[0] * volumes[0];

        for (int i = 1; i < n; i++)
        {
            typicals[i] = (highs[i] + lows[i] + closes[i]) / 3d;
            moneyFlow[i] = typicals[i] * volumes[i];
            if (typicals[i] > typicals[i - 1])
            {
                positive[i] = moneyFlow[i];
            }
            else if (typicals[i] < typicals[i - 1])
            {
                negative[i] = moneyFlow[i];
            }
        }

        for (int i = 0; i < n; i++)
        {
            // need 'period' diffs → indices [i-period+1 .. i] with i >= period
            if (i < period)
            {
                result[i] = null;
                continue;
            }

            double pos = 0d, neg = 0d;
            for (int j = i - period + 1; j <= i; j++)
            {
                pos += positive[j];
                neg += negative[j];
            }

            if (neg == 0d)
            {
                result[i] = pos == 0d ? 50d : 100d;
            }
            else
            {
                var ratio = pos / neg;
                result[i] = 100d - (100d / (1d + ratio));
            }
        }

        return result;
    }

    public IReadOnlyList<double?> ComputeAo(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        int shortPeriod = 5,
        int longPeriod = 34)
    {
        ValidateOhl(highs, lows);
        ValidatePositivePeriods(shortPeriod, longPeriod);
        if (shortPeriod >= longPeriod)
        {
            throw new ArgumentException("shortPeriod must be less than longPeriod.");
        }

        var medianPrices = Enumerable.Range(0, highs.Count)
            .Select(i => (highs[i] + lows[i]) / 2d)
            .ToArray();

        var shortSma = ComputeSma(medianPrices, shortPeriod);
        var longSma = ComputeSma(medianPrices, longPeriod);
        var ao = Enumerable.Repeat<double?>(null, highs.Count).ToArray();

        for (int i = 0; i < highs.Count; i++)
        {
            if (shortSma[i].HasValue && longSma[i].HasValue)
            {
                ao[i] = shortSma[i]!.Value - longSma[i]!.Value;
            }
        }

        return ao;
    }

    public IReadOnlyList<double?> ComputeMom(IReadOnlyList<double> closes, int period = 10)
    {
        ValidateInputs(closes, period);

        var mom = Enumerable.Repeat<double?>(null, closes.Count).ToArray();
        for (int i = period; i < closes.Count; i++)
        {
            mom[i] = closes[i] - closes[i - period];
        }

        return mom;
    }

    public IReadOnlyList<MacdPoint> ComputeMacd(
        IReadOnlyList<double> closes,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9)
    {
        ValidateInputs(closes, fastPeriod);
        ValidatePositivePeriods(slowPeriod, signalPeriod);
        if (fastPeriod >= slowPeriod)
        {
            throw new ArgumentException("fastPeriod must be less than slowPeriod.");
        }

        var fastEma = ComputeEma(closes, fastPeriod);
        var slowEma = ComputeEma(closes, slowPeriod);
        var macdLine = Enumerable.Repeat<double?>(null, closes.Count).ToArray();

        for (int i = 0; i < closes.Count; i++)
        {
            if (fastEma[i].HasValue && slowEma[i].HasValue)
            {
                macdLine[i] = fastEma[i]!.Value - slowEma[i]!.Value;
            }
        }

        var signal = ComputeEmaOnNullable(macdLine, signalPeriod);
        return Enumerable.Range(0, closes.Count)
            .Select(i =>
            {
                double? histogram = null;
                if (macdLine[i].HasValue && signal[i].HasValue)
                {
                    histogram = macdLine[i]!.Value - signal[i]!.Value;
                }

                return new MacdPoint(macdLine[i], signal[i], histogram);
            })
            .ToArray();
    }

    public IReadOnlyList<StochasticPoint> ComputeStochf(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int kPeriod = 14,
        int dPeriod = 3)
    {
        return ComputeStoch(highs, lows, closes, kPeriod, dPeriod);
    }

    public IReadOnlyList<double?> ComputeWillR(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int period = 14)
    {
        ValidateOhlc(highs, lows, closes);
        ValidatePositivePeriods(period);

        var willR = Enumerable.Repeat<double?>(null, closes.Count).ToArray();
        for (int i = period - 1; i < closes.Count; i++)
        {
            var highest = Max(highs, i - period + 1, i);
            var lowest = Min(lows, i - period + 1, i);
            var range = highest - lowest;
            willR[i] = range == 0d ? -50d : -100d * ((highest - closes[i]) / range);
        }

        return willR;
    }

    public IReadOnlyList<int> ComputeKickingByLength(
        IReadOnlyList<double> opens,
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        double minBodyToRangeRatio = 0.8d)
    {
        ValidateOhlc(opens, highs, lows, closes);
        if (minBodyToRangeRatio <= 0d || minBodyToRangeRatio > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(minBodyToRangeRatio), "Ratio must be in range (0, 1].");
        }

        var result = Enumerable.Repeat(0, closes.Count).ToArray();
        for (int i = 1; i < closes.Count; i++)
        {
            var prevBull = IsMarubozu(opens[i - 1], highs[i - 1], lows[i - 1], closes[i - 1], minBodyToRangeRatio, true);
            var prevBear = IsMarubozu(opens[i - 1], highs[i - 1], lows[i - 1], closes[i - 1], minBodyToRangeRatio, false);
            var currBull = IsMarubozu(opens[i], highs[i], lows[i], closes[i], minBodyToRangeRatio, true);
            var currBear = IsMarubozu(opens[i], highs[i], lows[i], closes[i], minBodyToRangeRatio, false);

            var prevBodyMax = Math.Max(opens[i - 1], closes[i - 1]);
            var prevBodyMin = Math.Min(opens[i - 1], closes[i - 1]);
            var currBodyMax = Math.Max(opens[i], closes[i]);
            var currBodyMin = Math.Min(opens[i], closes[i]);

            var bullishKick = prevBear && currBull && currBodyMin > prevBodyMax;
            var bearishKick = prevBull && currBear && currBodyMax < prevBodyMin;

            result[i] = bullishKick ? 100 : bearishKick ? -100 : 0;
        }

        return result;
    }

    private static void ValidateInputs(IReadOnlyList<double> closes, int period)
    {
        if (closes is null)
        {
            throw new ArgumentNullException(nameof(closes));
        }

        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be greater than zero.");
        }
    }

    private static void ValidateOhl(IReadOnlyList<double> highs, IReadOnlyList<double> lows)
    {
        if (highs is null)
        {
            throw new ArgumentNullException(nameof(highs));
        }

        if (lows is null)
        {
            throw new ArgumentNullException(nameof(lows));
        }

        if (highs.Count != lows.Count)
        {
            throw new ArgumentException("Highs and lows must have the same number of items.");
        }
    }

    private static void ValidateOhlc(IReadOnlyList<double> highs, IReadOnlyList<double> lows, IReadOnlyList<double> closes)
    {
        ValidateOhl(highs, lows);
        if (closes is null)
        {
            throw new ArgumentNullException(nameof(closes));
        }

        if (highs.Count != closes.Count)
        {
            throw new ArgumentException("Highs, lows and closes must have the same number of items.");
        }
    }

    private static void ValidateOhlc(IReadOnlyList<double> opens, IReadOnlyList<double> highs, IReadOnlyList<double> lows, IReadOnlyList<double> closes)
    {
        if (opens is null)
        {
            throw new ArgumentNullException(nameof(opens));
        }

        ValidateOhlc(highs, lows, closes);
        if (opens.Count != highs.Count)
        {
            throw new ArgumentException("Opens, highs, lows and closes must have the same number of items.");
        }
    }

    private static void ValidatePositivePeriods(params int[] periods)
    {
        if (periods.Any(p => p <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(periods), "All periods must be greater than zero.");
        }
    }

    private static double Max(IReadOnlyList<double> values, int start, int end)
    {
        var max = double.MinValue;
        for (int i = start; i <= end; i++)
        {
            if (values[i] > max)
            {
                max = values[i];
            }
        }

        return max;
    }

    private static double Min(IReadOnlyList<double> values, int start, int end)
    {
        var min = double.MaxValue;
        for (int i = start; i <= end; i++)
        {
            if (values[i] < min)
            {
                min = values[i];
            }
        }

        return min;
    }

    private static IReadOnlyList<double?> MidpointOfRange(IReadOnlyList<double> highs, IReadOnlyList<double> lows, int period)
    {
        var result = Enumerable.Repeat<double?>(null, highs.Count).ToArray();
        for (int i = period - 1; i < highs.Count; i++)
        {
            var highest = Max(highs, i - period + 1, i);
            var lowest = Min(lows, i - period + 1, i);
            result[i] = (highest + lowest) / 2d;
        }

        return result;
    }

    private static bool IsMarubozu(double open, double high, double low, double close, double minBodyToRangeRatio, bool bullish)
    {
        if (bullish && close <= open)
        {
            return false;
        }

        if (!bullish && close >= open)
        {
            return false;
        }

        var range = high - low;
        if (range <= 0d)
        {
            return false;
        }

        var body = Math.Abs(close - open);
        return (body / range) >= minBodyToRangeRatio;
    }

    private static IReadOnlyList<double?> ComputeSmaOnNullable(IReadOnlyList<double?> values, int period)
    {
        ValidatePositivePeriods(period);
        var result = Enumerable.Repeat<double?>(null, values.Count).ToArray();

        for (int i = period - 1; i < values.Count; i++)
        {
            var window = values.Skip(i - period + 1).Take(period).ToArray();
            if (window.Any(v => !v.HasValue))
            {
                continue;
            }

            result[i] = window.Average(v => v!.Value);
        }

        return result;
    }

    private static IReadOnlyList<double?> ComputeWma(IReadOnlyList<double> values, int period)
    {
        ValidatePositivePeriods(period);
        var result = Enumerable.Repeat<double?>(null, values.Count).ToArray();
        var denominator = period * (period + 1) / 2d;

        for (int i = period - 1; i < values.Count; i++)
        {
            double weightedSum = 0d;
            int weight = 1;
            for (int j = i - period + 1; j <= i; j++)
            {
                weightedSum += values[j] * weight;
                weight++;
            }

            result[i] = weightedSum / denominator;
        }

        return result;
    }

    private static IReadOnlyList<double?> ComputeWma(IReadOnlyList<double?> values, int period)
    {
        ValidatePositivePeriods(period);
        var result = Enumerable.Repeat<double?>(null, values.Count).ToArray();
        var denominator = period * (period + 1) / 2d;

        for (int i = period - 1; i < values.Count; i++)
        {
            var window = values.Skip(i - period + 1).Take(period).ToArray();
            if (window.Any(v => !v.HasValue))
            {
                continue;
            }

            double weightedSum = 0d;
            int weight = 1;
            foreach (var value in window)
            {
                weightedSum += value!.Value * weight;
                weight++;
            }

            result[i] = weightedSum / denominator;
        }

        return result;
    }

    private static IReadOnlyList<double?> ComputeEmaOnNullable(IReadOnlyList<double?> values, int period)
    {
        ValidatePositivePeriods(period);
        var result = Enumerable.Repeat<double?>(null, values.Count).ToArray();
        var firstSeedIndex = values
            .Select((v, i) => new { Value = v, Index = i })
            .Where(x => x.Value.HasValue)
            .Select(x => x.Index)
            .FirstOrDefault(-1);

        if (firstSeedIndex < 0)
        {
            return result;
        }

        var contiguous = new List<double>();
        for (int i = firstSeedIndex; i < values.Count; i++)
        {
            if (!values[i].HasValue)
            {
                break;
            }

            contiguous.Add(values[i]!.Value);
        }

        if (contiguous.Count < period)
        {
            return result;
        }

        var seed = contiguous.Take(period).Average();
        var seedIndex = firstSeedIndex + period - 1;
        result[seedIndex] = seed;

        var alpha = 2d / (period + 1d);
        for (int i = seedIndex + 1; i < values.Count; i++)
        {
            if (!values[i].HasValue || !result[i - 1].HasValue)
            {
                continue;
            }

            var previous = result[i - 1]!.Value;
            result[i] = ((values[i]!.Value - previous) * alpha) + previous;
        }

        return result;
    }

    private static double ToRsi(double avgGain, double avgLoss)
    {
        if (avgLoss == 0d && avgGain == 0d)
        {
            return 50d;
        }

        if (avgLoss == 0d)
        {
            return 100d;
        }

        var rs = avgGain / avgLoss;
        return 100d - (100d / (1d + rs));
    }
}
