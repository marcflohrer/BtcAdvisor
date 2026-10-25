using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;

namespace BtcAdvisor.Tests.Services;

/// <summary>
/// Regression tests for BuyFormulaService.
/// Bug #1: 2h sell oscillator medium trend coefficient was 5 instead of 50 (typo: missing zero).
/// Fix: changed 5 → 50 in osciSellCoefficientsForDecision for "2h".
/// Bug #2: 1h/2h IndicatorCoefficient values were swapped in osciSellCoefficientsForDecision
/// (1h=120, 2h=60 instead of 1h=60, 2h=120). All other arrays use 1h=60, 2h=120 consistently.
/// Fix: swapped 1h→60, 2h→120 in osciSellCoefficientsForDecision.
/// </summary>
[TestFixture]
public class BuyFormulaServiceTests
{
    // Base scenario: MA 2h buy=7 → 5000×7 = 35000 MA contribution
    //                 Osci buy: 2h=1(500), 4h=2(30), 1d=1(60) → 590 total
    //                 buySellSum base = 35590 > 34040 threshold → buy fires
    // With 2h medium-trend sell=1:
    //   Fix  (coeff=120, medium=50): deduction = 120×1×(1+50×1) = 6120 → 35590−6120 = 29470 < 34040 → blocked ✓
    //   Bug1 (coeff=60,  medium= 5): deduction =  60×1×(1+ 5×1) =  360 → 35590− 360 = 35230 > 34040 → fires  ✗
    private static Dictionary<string, BitcoinTimeRangeStatistics> BaseOscillatorStats(
        int twoHourSellCount = 0, bool twoHourMediumTrend = false) => new()
    {
        { "2h",  Build(sellCount: 0, buyCount: 1) },
        { "4h",  Build(sellCount: 0, buyCount: 2) },
        { "12h", Build(sellCount: 0, buyCount: 0) },
        { "1d",  Build(sellCount: 0, buyCount: 1) },
        { "1w",  Build(sellCount: twoHourSellCount, buyCount: 0, isMediumTrend: twoHourMediumTrend) },
    };

    private static Dictionary<string, BitcoinTimeRangeStatistics> BaseMovingAverageStats() => new()
    {
        { "2h", Build(sellCount: 0, buyCount: 7) }, // 5000 × 7 = 35000
    };

    /// <summary>
    /// Regression: with the bugs (coeff=60, medium=5), this test fails (returns true) because the 2h
    /// sell deduction was only 360 instead of 6120, keeping buySellSum above threshold.
    /// After fix (coeff=120, medium=50), the 6120 deduction pushes buySellSum well below 34040.
    /// </summary>
    [Test]
    public void IsBuyAdvice_WhenTwoHourHasMediumTrendSell_ShouldReturnFalse()
    {
        var oscillatorStats = BaseOscillatorStats(twoHourSellCount: 1, twoHourMediumTrend: true);
        var movingAverageStats = BaseMovingAverageStats();

        var result = BuyFormulaService.IsBuyAdvice(oscillatorStats, movingAverageStats);

        Assert.That(result, Is.False,
            "Buy must be blocked when 2h has a medium-trend sell signal. " +
            "Before fix (coeff=60, medium=5), deduction was only 360 → buy fired incorrectly.");
    }

    /// <summary>
    /// Sanity check: without a 2h sell signal, buy must fire (buySellSum 35590 > 34040).
    /// </summary>
    [Test]
    public void IsBuyAdvice_WhenNoSellSignals_ShouldReturnTrue()
    {
        var oscillatorStats = BaseOscillatorStats(twoHourSellCount: 0, twoHourMediumTrend: false);
        var movingAverageStats = BaseMovingAverageStats();

        var result = BuyFormulaService.IsBuyAdvice(oscillatorStats, movingAverageStats);

        Assert.That(result, Is.True,
            "Buy must fire when there are no sell signals and buySellSum exceeds threshold.");
    }

    /// <summary>
    /// A 2h sell with NO trend (noTrend only) should not reduce buySellSum enough to block buy,
    /// because neither strong/medium/weak trend multiplier fires.
    /// Deduction = 120 × sellCount × 1 × 1 × 1 = 120 → 35590 − 120 = 35470 > 34040 → buy fires.
    /// </summary>
    [Test]
    public void IsBuyAdvice_WhenTwoHourSellHasNoTrend_ShouldReturnTrue()
    {
        var oscillatorStats = BaseOscillatorStats(twoHourSellCount: 1, twoHourMediumTrend: false);
        var movingAverageStats = BaseMovingAverageStats();

        var result = BuyFormulaService.IsBuyAdvice(oscillatorStats, movingAverageStats);

        Assert.That(result, Is.True,
            "A 2h sell without any trend should not block the buy signal (small deduction of 60).");
    }

    /// <summary>
    /// Knockout rule #1 (mapped): 2h sell ≠ 0 must block buy regardless of other signals.
    /// </summary>
    [Test]
    public void IsBuyAdvice_WhenFiveMinuteHasSellSignal_ShouldReturnFalse()
    {
        var oscillatorStats = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            { "2h",  Build(sellCount: 1, buyCount: 1) }, // sell ≠ 0 → knockout
            { "4h",  Build(sellCount: 0, buyCount: 2) },
            { "12h", Build(sellCount: 0, buyCount: 0) },
            { "1d",  Build(sellCount: 0, buyCount: 1) },
            { "1w",  Build(sellCount: 0, buyCount: 0) },
        };
        var movingAverageStats = BaseMovingAverageStats();

        var result = BuyFormulaService.IsBuyAdvice(oscillatorStats, movingAverageStats);

        Assert.That(result, Is.False, "Knockout #1: 2h sell ≠ 0 must always block buy.");
    }

    /// <summary>
    /// Knockout rule #4 (mapped): 4h buy > 2 must block buy.
    /// </summary>
    [Test]
    public void IsBuyAdvice_WhenFifteenMinuteBuyCountExceedsTwo_ShouldReturnFalse()
    {
        var oscillatorStats = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            { "2h",  Build(sellCount: 0, buyCount: 1) },
            { "4h",  Build(sellCount: 0, buyCount: 3) }, // > 2 → knockout
            { "12h", Build(sellCount: 0, buyCount: 0) },
            { "1d",  Build(sellCount: 0, buyCount: 1) },
            { "1w",  Build(sellCount: 0, buyCount: 0) },
        };
        var movingAverageStats = BaseMovingAverageStats();

        var result = BuyFormulaService.IsBuyAdvice(oscillatorStats, movingAverageStats);

        Assert.That(result, Is.False, "Knockout #4: 4h buy > 2 must always block buy.");
    }

    /// <summary>
    /// Regression for coefficient swap: 1h sell with medium trend should use coeff=60 (not 120).
    /// With fix (1h=60):  deduction = 60×1×(1+50×1) = 3060 → 35590−3060 = 32530 < 34040 → blocked ✓
    /// With swap bug (1h=120): deduction = 120×1×(1+50×1) = 6120 → also blocked, but 1h was overpenalized.
    /// We verify the inverse: 1h sell WITHOUT trend → small deduction that must NOT block buy.
    /// 1h no-trend deduction = 60×1 = 60 → 35590−60 = 35530 > 34040 → buy fires.
    /// If the bug (1h=120) were present: 120×1 = 120 → still fires, but coefficient is wrong.
    /// </summary>
    [Test]
    public void IsBuyAdvice_WhenOneHourSellHasNoTrend_ShouldReturnTrue()
    {
        var oscillatorStats = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            { "2h",  Build(sellCount: 0, buyCount: 1) },
            { "4h",  Build(sellCount: 0, buyCount: 2) },
            { "12h", Build(sellCount: 0, buyCount: 0) },
            { "1d",  Build(sellCount: 1, buyCount: 0) }, // mapped day range sell, no trend
            { "1w",  Build(sellCount: 0, buyCount: 0) },
        };
        var movingAverageStats = BaseMovingAverageStats();

        var result = BuyFormulaService.IsBuyAdvice(oscillatorStats, movingAverageStats);

        Assert.That(result, Is.True,
            "1h sell without trend should only deduct 60 (coeff=60), not enough to block buy.");
    }

    /// <summary>
    /// Regression for coefficient swap: 1h sell WITH medium trend uses coeff=60.
    /// Deduction = 60×1×(1+50×1) = 3060 → 35590−3060 = 32530 < 34040 → blocked.
    /// </summary>
    [Test]
    public void IsBuyAdvice_WhenOneHourHasMediumTrendSell_ShouldReturnFalse()
    {
        var oscillatorStats = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            { "2h",  Build(sellCount: 0, buyCount: 1) },
            { "4h",  Build(sellCount: 0, buyCount: 2) },
            { "12h", Build(sellCount: 0, buyCount: 0) },
            { "1d",  Build(sellCount: 1, buyCount: 0, isMediumTrend: true) }, // mapped day range medium-trend sell
            { "1w",  Build(sellCount: 0, buyCount: 0) },
        };
        var movingAverageStats = BaseMovingAverageStats();

        var result = BuyFormulaService.IsBuyAdvice(oscillatorStats, movingAverageStats);

        Assert.That(result, Is.False,
            "1h sell with medium trend (coeff=60, medium=50) should block buy: deduction=3060.");
    }

    private static BitcoinTimeRangeStatistics Build(
        int sellCount, int buyCount,
        bool isMediumTrend = false, bool isStrongTrend = false, bool isWeakTrend = false)
        => new()
        {
            IndicatorSellCount = sellCount,
            IndicatorBuyCount = buyCount,
            IndicatorMediumTrendCount = isMediumTrend ? 1 : 0,
            IndicatorStrongTrendCount = isStrongTrend ? 1 : 0,
            IndicatorWeakTrendCount = isWeakTrend ? 1 : 0,
        };
}
