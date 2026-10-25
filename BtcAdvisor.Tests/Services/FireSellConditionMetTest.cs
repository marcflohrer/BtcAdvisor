using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions;

namespace BtcAdvisor.Tests.Services;

[TestFixture]
public class FireSellConditionMetTest
{
    private List<string> timeRangeForDecision;

    [SetUp]
    public void Setup()
    {
        timeRangeForDecision = ["5m", "15m", "30m", "1h", "2h"];
    }

    [Test]
    public void IsConditionsMet_WhenStrongSellConditionIsMetAndProfitGreaterPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetStrongSellOscillatorStatistics();
        var movingAverageStatistics = GetStrongSellMovingAverageStatistics();

        var sut = new FireSellConditionMet();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 100, 90, Convert.ToDecimal(100.0 + double.Pi + 0.0000001));

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(sut.TradeAdvice.ToString(), Is.EqualTo("FireSellStrong"));
        }
    }

    [Test]
    public void IsConditionsMet_WhenStrictSellConditionIsMetAndProfitGreaterPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetStrictSellOscillatorStatistics();
        var movingAverageStatistics = GetStrictSellMovingAverageStatistics();

        var sut = new FireSellConditionMet();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 100, 100, Convert.ToDecimal(100.0 + double.Pi + 0.000001));

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(sut.TradeAdvice.ToString(), Is.EqualTo("FireSellStrict"));
        }
    }

    [Test]
    [Ignore("Sell formula")]
    public void IsConditionsMet_WhenSellFormulaConditionIsMetAndProfitGreaterPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetSellFormulaOscillatorStatistics();
        var movingAverageStatistics = GetSellFormulaMovingAverageStatistics();

        var sut = new FireSellConditionMet();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 100, 100, Convert.ToDecimal(100.0 + double.Pi + 0.000001));

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsConditionsMet_WhenStrongSellConditionIsMetAndProfitLessThanPi_ShouldReturnFalse()
    {
        // Arrange
        Dictionary<string, BitcoinTimeRangeStatistics> oscillatorStatistics = GetStrongSellOscillatorStatistics();
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics = GetStrongSellMovingAverageStatistics();

        var sut = new FireSellConditionMet();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 100, 100, Convert.ToDecimal(100.0 + double.Pi - 0.000001));

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsConditionsMet_WhenSellFormulaConditionIsMetAndProfitLessThanPi_ShouldReturnFalse()
    {
        // Arrange
        var oscillatorStatistics = GetSellFormulaOscillatorStatistics();
        var movingAverageStatistics = GetSellFormulaMovingAverageStatistics();
        var sut = new FireSellConditionMet();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 100, 100, Convert.ToDecimal(100.0 + double.Pi - 0.0000001));

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsConditionsMet_WhenSellConditionsAreNotMetAndProfitGreaterPi_ShouldReturnFalse()
    {
        // Arrange
        var oscillatorStatistics = GetBuyFormulaOscillatorStatistics();
        var movingAverageStatistics = GetBuyFormulaMovingAverageStatistics();
        var sut = new FireSellConditionMet();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 100, 100, Convert.ToDecimal(100.0 + double.Pi + 0.0000001));

        // Assert
        Assert.That(result, Is.False);
    }

    // [TestCase(/*oscillator*//*5m*/0,0, /*Trend*/1,0,0,0,/*15m*/2,0, /*Trend*/1,0,0,0,/*30m*/3,0, /*Trend*/1,0,0,0,/*1h*/3,0, /*Trend*/1,0,0,0,/*2h*/3,0, /*Trend*/1,0,0,0,/*movingaverage*/
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetStrongSellOscillatorStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(0, 0, true) },
        { "15m", BuildBitcoinTimeRangeStatistics(2, 0, true) },
        { "30m", BuildBitcoinTimeRangeStatistics(3, 0, true) },
        { "1h", BuildBitcoinTimeRangeStatistics(3, 0, true) },
        { "2h", BuildBitcoinTimeRangeStatistics(3, 0, true) },
    };

    // /*5m*/1,6, /*Trend*/0,0,0,0,/*15m*/1,6, /*Trend*/0,0,0,0,/*30m*/1,6, /*Trend*/0,0,0,0,/*1h*/1,6, /*Trend*/0,0,0,0,/*2h*/0,7, /*Trend*/0,0,0,0,"StrongSell")]
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetStrongSellMovingAverageStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(1, 6) },
        { "15m", BuildBitcoinTimeRangeStatistics(1, 6) },
        { "30m", BuildBitcoinTimeRangeStatistics(1, 6) },
        { "1h", BuildBitcoinTimeRangeStatistics(1, 6) },
        { "2h", BuildBitcoinTimeRangeStatistics(0, 7) },
    };

    // 2024-11-10 20:19: Verkaufen (verschärft) ($500) ($¤80,317.50) Gewinn: $ 597.5 seit Kauf: 11/10/2024 17:31:10
    // [TestCase(/*oscillator*//*5m*/2,0, /*Trend*/0,0,0,1,/*15m*/2,0, /*Trend*/1,0,0,0,/*30m*/0,0, /*Trend*/1,0,0,0,/*1h*/1,0, /*Trend*/0,0,0,1,/*2h*/1,0, /*Trend*/1,0,0,0,
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetStrictSellOscillatorStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(2, 0) },
        { "15m", BuildBitcoinTimeRangeStatistics(2, 0, true) },
        { "30m", BuildBitcoinTimeRangeStatistics(0, 0, true) },
        { "1h", BuildBitcoinTimeRangeStatistics(1, 0) },
        { "2h", BuildBitcoinTimeRangeStatistics(1, 0, true) },
    };

    // /*movingaverage*//*5m*/1,6, /*Trend*/0,0,0,0,/*15m*/2,5, /*Trend*/0,0,0,0,/*30m*/2,5, /*Trend*/0,0,0,0,/*1h*/3,4, /*Trend*/0,0,0,0,/*2h*/3,3, /*Trend*/0,0,0,0,"StrictSell")]
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetStrictSellMovingAverageStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(1, 6) },
        { "15m", BuildBitcoinTimeRangeStatistics(2, 5) },
        { "30m", BuildBitcoinTimeRangeStatistics(2, 5) },
        { "1h", BuildBitcoinTimeRangeStatistics(3, 4) },
        { "2h", BuildBitcoinTimeRangeStatistics(3, 3) },
    };

    // [TestCase(/*oscillator*//*5m*/1,0, /*Trend*/0,0,0,1,/*15m*/0,0, /*Trend*/1,0,0,0,/*30m*/0,1, /*Trend*/0,1,0,0,/*1h*/0,1, /*Trend*/1,0,0,0,/*2h*/0,1, /*Trend*/0,0,0,1,
    // /*movingaverage*//*5m*/2,4, /*Trend*/0,0,0,0,/*15m*/3,3, /*Trend*/0,0,0,0,/*30m*/5,2, /*Trend*/0,0,0,0,/*1h*/5,2, /*Trend*/0,0,0,0,/*2h*/3,3, /*Trend*/0,0,0,0,"Sell")]
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetSellFormulaOscillatorStatistics() => new()
    {
        // /*5m*/1,0, /*Trend*/0,0,0,1,
        { "5m", BuildBitcoinTimeRangeStatistics(1, 0) },
        // /*15m*/0,0, /*Trend*/1,0,0,0,
        { "15m", BuildBitcoinTimeRangeStatistics(0, 0, true) },
        // /*30m*/0,1, /*Trend*/0,1,0,0,
        { "30m", BuildBitcoinTimeRangeStatistics(0, 1, false, true) },
        // ,/*1h*/0,1, /*Trend*/1,0,0,0,
        { "1h", BuildBitcoinTimeRangeStatistics(0, 1, true) },
        // /*2h*/0,1, /*Trend*/0,0,0,1
        { "2h", BuildBitcoinTimeRangeStatistics(0, 1, true) },
    };

    // /*movingaverage*//*5m*/2,4, /*Trend*/0,0,0,0,/*15m*/3,3, /*Trend*/0,0,0,0,/*30m*/5,2, /*Trend*/0,0,0,0,/*1h*/5,2, /*Trend*/0,0,0,0,/*2h*/3,3, /*Trend*/0,0,0,0,"Sell")]
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetSellFormulaMovingAverageStatistics() => new()
    {
        // /*5m*/2,4,
        { "5m", BuildBitcoinTimeRangeStatistics(2, 4) },
        // /*15m*/3,3,
        { "15m", BuildBitcoinTimeRangeStatistics(3, 3) },
        // /*30m*/5,2,
        { "30m", BuildBitcoinTimeRangeStatistics(5, 2) },
        // /*1h*/5,2, 
        { "1h", BuildBitcoinTimeRangeStatistics(5, 2) },
        // /*2h*/3,3,
        { "2h", BuildBitcoinTimeRangeStatistics(3, 3) },
    };

    // [TestCase(/*oscillator*
    // //*5m*/0,1, /*Trend*/0,0,1,0,/*15m*/2,1, /*Trend*/0,0,0,1,/*30m*/2,0, /*Trend*/0,0,1,0,/*1h*/1,2, /*Trend*/0,0,1,0,/*2h*/1,1, /*Trend*/0,0,0,1,
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetBuyFormulaOscillatorStatistics() => new()
    {
        // //*5m*/0,1, /*Trend*/0,0,1,0,
        { "5m", BuildBitcoinTimeRangeStatistics(1, 0, false, false, true) },
        // /*15m*/2,1, /*Trend*/0,0,0,1,
        { "15m", BuildBitcoinTimeRangeStatistics(2, 1) },
        // /*30m*/2,0, /*Trend*/0,0,1,0,
        { "30m", BuildBitcoinTimeRangeStatistics(2, 0, false, false, true) },
        // /*1h*/1,2, /*Trend*/0,0,1,0,
        { "1h", BuildBitcoinTimeRangeStatistics(1, 2, false, false, true) },
        // /*2h*/1,1, /*Trend*/0,0,0,1
        { "2h", BuildBitcoinTimeRangeStatistics(1, 1) },
    };

    // /*movingaverage*//*5m*/0,7, /*Trend*/0,0,0,0,/*15m*/1,6, /*Trend*/0,0,0,0,/*30m*/0,7, /*Trend*/0,0,0,0,/*1h*/0,7, /*Trend*/0,0,0,0,/*2h*/1,6, /*Trend*/0,0,0,0,"Buy")]
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetBuyFormulaMovingAverageStatistics() => new()
    {
        // /*5m*/0,7,
        { "5m", BuildBitcoinTimeRangeStatistics(0, 7) },
        // /*15m*/1,6,
        { "15m", BuildBitcoinTimeRangeStatistics(1, 6) },
        // /*30m*/0,7, 
        { "30m", BuildBitcoinTimeRangeStatistics(0, 7) },
        // /*1h*/0,7,
        { "1h", BuildBitcoinTimeRangeStatistics(0, 7) },
        // /*2h*/1,6,
        { "2h", BuildBitcoinTimeRangeStatistics(1, 6) },
    };

    private static BitcoinTimeRangeStatistics BuildBitcoinTimeRangeStatistics(int sellCount, int buyCount, bool isStrongTrend = false, bool isMediumTrend = false, bool isWeakTrend = false)
    {
        var trendCount = 0;
        if (isStrongTrend)
        {
            trendCount++;
        }
        if (isMediumTrend)
        {
            trendCount++;
        }
        if (isWeakTrend)
        {
            trendCount++;
        }
        if(trendCount > 1)
        {
            throw new ArgumentException("Not more than one trend!");
        }
        return new BitcoinTimeRangeStatistics {
            IndicatorSellCount = sellCount,
            IndicatorBuyCount = buyCount,
            IndicatorStrongTrendCount = isStrongTrend ? 1 : 0,
            IndicatorMediumTrendCount = isMediumTrend ? 1 : 0,
            IndicatorWeakTrendCount = isWeakTrend ? 1 : 0
        };
    }
}
