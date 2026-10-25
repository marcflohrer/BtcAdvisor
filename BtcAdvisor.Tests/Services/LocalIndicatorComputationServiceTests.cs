using BtcAdvisor.Services;

namespace BtcAdvisor.Tests.Services;

public class LocalIndicatorComputationServiceTests
{
    private LocalIndicatorComputationService _service = null!;

    [SetUp]
    public void Setup()
    {
        _service = new LocalIndicatorComputationService();
    }

    [Test]
    public void ComputeSma_WithPeriodThree_ReturnsExpectedSeries()
    {
        var closes = new[] { 1d, 2d, 3d, 4d, 5d };

        var sma = _service.ComputeSma(closes, 3);

        Assert.That(sma[0], Is.Null);
        Assert.That(sma[1], Is.Null);
        Assert.That(sma[2], Is.EqualTo(2d).Within(1e-9));
        Assert.That(sma[3], Is.EqualTo(3d).Within(1e-9));
        Assert.That(sma[4], Is.EqualTo(4d).Within(1e-9));
    }

    [Test]
    public void ComputeEma_WithPeriodThree_ReturnsExpectedSeries()
    {
        var closes = new[] { 1d, 2d, 3d, 4d, 5d };

        var ema = _service.ComputeEma(closes, 3);

        Assert.That(ema[0], Is.Null);
        Assert.That(ema[1], Is.Null);
        Assert.That(ema[2], Is.EqualTo(2d).Within(1e-9));
        Assert.That(ema[3], Is.EqualTo(3d).Within(1e-9));
        Assert.That(ema[4], Is.EqualTo(4d).Within(1e-9));
    }

    [Test]
    public void ComputeRsi_WithStrictUptrend_StaysAtHundredAfterWarmup()
    {
        var closes = new[] { 1d, 2d, 3d, 4d, 5d };

        var rsi = _service.ComputeRsi(closes, 3);

        Assert.That(rsi[0], Is.Null);
        Assert.That(rsi[1], Is.Null);
        Assert.That(rsi[2], Is.Null);
        Assert.That(rsi[3], Is.EqualTo(100d).Within(1e-9));
        Assert.That(rsi[4], Is.EqualTo(100d).Within(1e-9));
    }

    [Test]
    public void ComputeRsi_WithAlternatingMoves_ReturnsSmoothedWilderValues()
    {
        var closes = new[] { 1d, 2d, 1d, 2d, 1d };

        var rsi = _service.ComputeRsi(closes, 2);

        Assert.That(rsi[0], Is.Null);
        Assert.That(rsi[1], Is.Null);
        Assert.That(rsi[2], Is.EqualTo(50d).Within(1e-9));
        Assert.That(rsi[3], Is.EqualTo(75d).Within(1e-9));
        Assert.That(rsi[4], Is.EqualTo(37.5d).Within(1e-9));
    }

    [Test]
    public void ComputeSma_WithInvalidPeriod_Throws()
    {
        var closes = new[] { 1d, 2d, 3d };

        Assert.That(() => _service.ComputeSma(closes, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ComputeMom_WithPeriodTwo_ReturnsExpectedDelta()
    {
        var closes = new[] { 10d, 12d, 15d, 14d };

        var mom = _service.ComputeMom(closes, 2);

        Assert.That(mom[0], Is.Null);
        Assert.That(mom[1], Is.Null);
        Assert.That(mom[2], Is.EqualTo(5d).Within(1e-9));
        Assert.That(mom[3], Is.EqualTo(2d).Within(1e-9));
    }

    [Test]
    public void ComputeMacd_OnUptrend_ProducesPositiveMacdAndHistogram()
    {
        var closes = Enumerable.Range(1, 80).Select(i => (double)i).ToArray();

        var macd = _service.ComputeMacd(closes, 12, 26, 9);
        var last = macd[^1];

        Assert.That(last.Macd, Is.Not.Null);
        Assert.That(last.Signal, Is.Not.Null);
        Assert.That(last.Histogram, Is.Not.Null);
        Assert.That(last.Macd!.Value, Is.GreaterThan(0d));
    }

    [Test]
    public void ComputeWillR_StaysWithinExpectedBounds()
    {
        var highs = Enumerable.Range(1, 40).Select(i => 100d + i).ToArray();
        var lows = Enumerable.Range(1, 40).Select(i => 90d + i).ToArray();
        var closes = Enumerable.Range(1, 40).Select(i => 95d + i).ToArray();

        var willR = _service.ComputeWillR(highs, lows, closes, 14);
        var values = willR.Where(v => v.HasValue).Select(v => v!.Value).ToArray();

        Assert.That(values.Length, Is.GreaterThan(0));
        Assert.That(values.All(v => v is <= 0d and >= -100d), Is.True);
    }

    [Test]
    public void ComputeKickingByLength_DetectsBullishAndBearishSignals()
    {
        var opens = new[] { 10d, 13d, 20d, 15d };
        var highs = new[] { 10.2d, 16.2d, 24.2d, 15.2d };
        var lows = new[] { 7.8d, 12.8d, 19.8d, 10.8d };
        var closes = new[] { 8d, 16d, 24d, 11d };

        var kicks = _service.ComputeKickingByLength(opens, highs, lows, closes, 0.8d);

        Assert.That(kicks[1], Is.EqualTo(100));
        Assert.That(kicks[3], Is.EqualTo(-100));
    }

    [Test]
    public void ComputeIchimoku_WithEnoughData_PopulatesCoreLines()
    {
        var highs = Enumerable.Range(1, 120).Select(i => 100d + i).ToArray();
        var lows = Enumerable.Range(1, 120).Select(i => 90d + i).ToArray();
        var closes = Enumerable.Range(1, 120).Select(i => 95d + i).ToArray();

        var ichimoku = _service.ComputeIchimoku(highs, lows, closes);

        Assert.That(ichimoku[25].TenkanSen, Is.Not.Null);
        Assert.That(ichimoku[25].KijunSen, Is.Not.Null);
        Assert.That(ichimoku[80].SenkouSpanA, Is.Not.Null);
        Assert.That(ichimoku[80].SenkouSpanB, Is.Not.Null);
    }

    [Test]
    public void ComputeStoch_And_Stochf_ReturnStructuredValues()
    {
        var highs = Enumerable.Range(1, 40).Select(i => 100d + i).ToArray();
        var lows = Enumerable.Range(1, 40).Select(i => 90d + i).ToArray();
        var closes = Enumerable.Range(1, 40).Select(i => 95d + i).ToArray();

        var stoch = _service.ComputeStoch(highs, lows, closes);
        var stochf = _service.ComputeStochf(highs, lows, closes);

        Assert.That(stoch[^1].K, Is.Not.Null);
        Assert.That(stoch[^1].D, Is.Not.Null);
        Assert.That(stochf[^1].K, Is.Not.Null);
        Assert.That(stochf[^1].D, Is.Not.Null);
    }

    [Test]
    public void ComputeCci_Adx_Ao_Hma_ReturnValuesAfterWarmup()
    {
        var highs = Enumerable.Range(1, 120).Select(i => 100d + i).ToArray();
        var lows = Enumerable.Range(1, 120).Select(i => 90d + i).ToArray();
        var closes = Enumerable.Range(1, 120).Select(i => 95d + i).ToArray();

        var cci = _service.ComputeCci(highs, lows, closes, 20);
        var adx = _service.ComputeAdx(highs, lows, closes, 14);
        var ao = _service.ComputeAo(highs, lows);
        var hma = _service.ComputeHma(closes, 20);

        Assert.That(cci[^1], Is.Not.Null);
        Assert.That(adx[^1], Is.Not.Null);
        Assert.That(ao[^1], Is.Not.Null);
        Assert.That(hma[^1], Is.Not.Null);
    }

    [Test]
    public void ComputeAtr_FlatBars_YieldsZero()
    {
        var bars = 30;
        var highs = Enumerable.Repeat(100d, bars).ToArray();
        var lows = Enumerable.Repeat(100d, bars).ToArray();
        var closes = Enumerable.Repeat(100d, bars).ToArray();

        var atr = _service.ComputeAtr(highs, lows, closes, 14);

        Assert.That(atr[13], Is.Null);
        Assert.That(atr[14], Is.EqualTo(0d).Within(1e-9));
        Assert.That(atr[^1], Is.EqualTo(0d).Within(1e-9));
    }

    [Test]
    public void ComputeAtr_ConstantTrueRange_ConvergesToThatRange()
    {
        // Each bar: high = i+2, low = i, close = i+1. TR = 2 (high-low) every bar.
        var bars = 50;
        var highs = Enumerable.Range(0, bars).Select(i => (double)(i + 2)).ToArray();
        var lows = Enumerable.Range(0, bars).Select(i => (double)i).ToArray();
        var closes = Enumerable.Range(0, bars).Select(i => (double)(i + 1)).ToArray();

        var atr = _service.ComputeAtr(highs, lows, closes, 14);

        // Initial ATR is mean of TR[1..14] = 2.0
        Assert.That(atr[14], Is.EqualTo(2d).Within(1e-9));
        // Wilder smoothing of constant TR=2 stays at 2
        Assert.That(atr[^1], Is.EqualTo(2d).Within(1e-9));
    }

    [Test]
    public void ComputeAtr_InsufficientData_ReturnsAllNulls()
    {
        var highs = new[] { 1d, 2d, 3d };
        var lows = new[] { 1d, 2d, 3d };
        var closes = new[] { 1d, 2d, 3d };

        var atr = _service.ComputeAtr(highs, lows, closes, 14);

        Assert.That(atr.All(v => v is null), Is.True);
    }

    [Test]
    public void ComputeBollinger_FlatSeries_HasZeroWidth()
    {
        var closes = Enumerable.Repeat(100d, 25).ToArray();

        var bb = _service.ComputeBollinger(closes, period: 20, stdDevMultiplier: 2.0);

        Assert.That(bb.Count, Is.EqualTo(25));
        for (var i = 0; i < 19; i++)
        {
            Assert.That(bb[i].Upper, Is.Null);
        }
        Assert.That(bb[24].Upper, Is.EqualTo(100d).Within(1e-9));
        Assert.That(bb[24].Lower, Is.EqualTo(100d).Within(1e-9));
        Assert.That(bb[24].WidthPercent, Is.EqualTo(0d).Within(1e-9));
    }

    [Test]
    public void ComputeBollinger_VariableSeries_ProducesPositiveWidth()
    {
        var closes = Enumerable.Range(1, 25).Select(i => (double)i).ToArray();

        var bb = _service.ComputeBollinger(closes, period: 20, stdDevMultiplier: 2.0);

        Assert.That(bb[19].Middle, Is.EqualTo(10.5d).Within(1e-9));
        Assert.That(bb[19].Upper!.Value, Is.GreaterThan(bb[19].Middle!.Value));
        Assert.That(bb[19].WidthPercent!.Value, Is.GreaterThan(0d));
    }

    [Test]
    public void ComputeObv_RisingCloses_AccumulatesPositively()
    {
        var closes = new[] { 10d, 11d, 12d, 13d };
        var volumes = new[] { 100d, 100d, 100d, 100d };

        var obv = _service.ComputeObv(closes, volumes);

        Assert.That(obv[0], Is.EqualTo(0d));
        Assert.That(obv[1], Is.EqualTo(100d));
        Assert.That(obv[2], Is.EqualTo(200d));
        Assert.That(obv[3], Is.EqualTo(300d));
    }

    [Test]
    public void ComputeObv_FallingCloses_AccumulatesNegatively()
    {
        var closes = new[] { 13d, 12d, 11d, 10d };
        var volumes = new[] { 100d, 50d, 25d, 25d };

        var obv = _service.ComputeObv(closes, volumes);

        Assert.That(obv[0], Is.EqualTo(0d));
        Assert.That(obv[1], Is.EqualTo(-50d));
        Assert.That(obv[2], Is.EqualTo(-75d));
        Assert.That(obv[3], Is.EqualTo(-100d));
    }

    [Test]
    public void ComputeObv_FlatCloses_DoesNotChange()
    {
        var closes = new[] { 10d, 10d, 10d };
        var volumes = new[] { 100d, 100d, 100d };

        var obv = _service.ComputeObv(closes, volumes);

        Assert.That(obv, Is.EqualTo(new[] { 0d, 0d, 0d }));
    }

    [Test]
    public void ComputeObv_LengthMismatch_Throws()
    {
        var closes = new[] { 1d, 2d };
        var volumes = new[] { 100d };

        Assert.Throws<ArgumentException>(() => _ = _service.ComputeObv(closes, volumes));
    }

    [Test]
    public void ComputeVwap_FlatSeries_EqualsClose()
    {
        var n = 10;
        var highs = Enumerable.Repeat(100d, n).ToArray();
        var lows = Enumerable.Repeat(100d, n).ToArray();
        var closes = Enumerable.Repeat(100d, n).ToArray();
        var volumes = Enumerable.Repeat(50d, n).ToArray();

        var vwap = _service.ComputeVwap(highs, lows, closes, volumes, period: 5);

        Assert.That(vwap[0], Is.Null);
        Assert.That(vwap[3], Is.Null);
        Assert.That(vwap[4]!.Value, Is.EqualTo(100d).Within(1e-9));
        Assert.That(vwap[9]!.Value, Is.EqualTo(100d).Within(1e-9));
    }

    [Test]
    public void ComputeVwap_VolumeWeighting_BiasesTowardHighVolumeBars()
    {
        // 5 bars, period 5 → single VWAP at index 4 weighted by volumes
        var highs = new[] { 10d, 20d, 30d, 40d, 50d };
        var lows = new[] { 10d, 20d, 30d, 40d, 50d };
        var closes = new[] { 10d, 20d, 30d, 40d, 50d };
        var volumes = new[] { 1d, 1d, 1d, 1d, 100d };

        var vwap = _service.ComputeVwap(highs, lows, closes, volumes, period: 5);

        // Heavily weighted toward bar #4 (price 50)
        Assert.That(vwap[4]!.Value, Is.GreaterThan(45d));
    }

    [Test]
    public void ComputeMfi_RisingSeries_ApproachesHundred()
    {
        var n = 20;
        var highs = Enumerable.Range(1, n).Select(i => (double)i + 0.5).ToArray();
        var lows = Enumerable.Range(1, n).Select(i => (double)i - 0.5).ToArray();
        var closes = Enumerable.Range(1, n).Select(i => (double)i).ToArray();
        var volumes = Enumerable.Repeat(100d, n).ToArray();

        var mfi = _service.ComputeMfi(highs, lows, closes, volumes, period: 14);

        Assert.That(mfi[13], Is.Null);
        Assert.That(mfi[19]!.Value, Is.EqualTo(100d).Within(1e-9));
    }

    [Test]
    public void ComputeMfi_FallingSeries_ApproachesZero()
    {
        var n = 20;
        var highs = Enumerable.Range(1, n).Select(i => (double)(n - i + 1) + 0.5).ToArray();
        var lows = Enumerable.Range(1, n).Select(i => (double)(n - i + 1) - 0.5).ToArray();
        var closes = Enumerable.Range(1, n).Select(i => (double)(n - i + 1)).ToArray();
        var volumes = Enumerable.Repeat(100d, n).ToArray();

        var mfi = _service.ComputeMfi(highs, lows, closes, volumes, period: 14);

        Assert.That(mfi[19]!.Value, Is.LessThan(5d));
    }

    [Test]
    public void ComputeMfi_LengthMismatch_Throws()
    {
        var highs = new[] { 1d };
        var lows = new[] { 1d };
        var closes = new[] { 1d };
        var volumes = new[] { 1d, 2d };

        Assert.Throws<ArgumentException>(() => _ = _service.ComputeMfi(highs, lows, closes, volumes));
    }
}
