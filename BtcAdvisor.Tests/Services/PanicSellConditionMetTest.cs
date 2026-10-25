using Moq;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests.Services;

[TestFixture]
public class PanicBuyConditionMetTests
{
    PanicBuyConditionMet sut = null!;

    [SetUp]
    public void TestInitialize()
    {
        sut = new PanicBuyConditionMet(new AdditionalPanicBuyCondition());
    }

    [Test]
    public void IsConditionsMet_WhenStrongBuyConditionIsMetAndProfitGreaterPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetStrongBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrongBuyMovingAverageStatistics();
        
        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 90, Convert.ToDecimal(100.0 + double.Pi + 0.0000001), 100, 95);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(sut.TradeAdvice.ToString(), Is.EqualTo("PanicBuy2k"));
        }
    }

    [Test]
    public void IsConditionsMet_WhenStrictBuyConditionIsMetAndProfitGreaterPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetStrictBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrictBuyMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 90, Convert.ToDecimal(100.0 + double.Pi + 0.000001), 100, 95);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(sut.TradeAdvice.ToString(), Is.EqualTo("PanicBuyStrict"));
        }
    }

    [Test]
    public void IsConditionsMet_WhenBuy2kConditionIsMetAndProfitGreaterPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetBuy2kOscillatorStatistics();
        var movingAverageStatistics = GetBuy2kMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 90, Convert.ToDecimal(100.0 + double.Pi + 0.000001), 100, 95);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(sut.TradeAdvice.ToString(), Is.EqualTo("PanicBuy2k"));
        }
    }

    [Test]
    public void IsConditionsMet_WhenBuyConditionIsMetAndProfitGreaterPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetBuyFormulaOscillatorStatistics();
        var movingAverageStatistics = GetBuyFormulaMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 90, Convert.ToDecimal(100.0 + double.Pi + 0.000001), 100, 95);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(sut.TradeAdvice.ToString(), Is.EqualTo("PanicBuy"));
        }
    }

    [Test]
    public void IsConditionsMet_WhenStrongBuyConditionIsMetAndProfitLessThanPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetStrongBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrongBuyMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 90, Convert.ToDecimal(100.0 + double.Pi - 0.0000001), 100, 95);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.False);
        }
    }

    [Test]
    public void IsConditionsMet_WhenStrictBuyConditionIsMetAndProfitLessThanPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetStrictBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrictBuyMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 90, Convert.ToDecimal(100.0 + double.Pi - 0.000001), 100, 95);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.False);
        }
    }

    [Test]
    public void IsConditionsMet_WhenBuy2kConditionIsMetAndProfitLessThanPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetBuy2kOscillatorStatistics();
        var movingAverageStatistics = GetBuy2kMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 90, Convert.ToDecimal(100.0 + double.Pi - 0.000001), 100);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsConditionsMet_WhenBuyConditionIsMetAndProfitLessThanPi_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetBuyFormulaOscillatorStatistics();
        var movingAverageStatistics = GetBuyFormulaMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(oscillatorStatistics, movingAverageStatistics, 90, Convert.ToDecimal(100.0 + double.Pi - 0.000001), 100, 95);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsConditionsMet_WhenIncreaseSinceLatestDropIsLessThanPiPercent_ShouldReturnFalse()
    {
        // Arrange
        var oscillatorStatistics = GetStrongBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrongBuyMovingAverageStatistics();

        decimal latestBuyAfterFireSell = 99;
        decimal currentPrice = 100;

        // Act
        var result = sut.IsConditionsMet(
            oscillatorStatistics,
            movingAverageStatistics,
            90,
            Convert.ToDecimal(100.0 + double.Pi + 0.000001),
            currentPrice,
            latestBuyAfterFireSell);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsConditionsMet_WhenIncreaseSinceLatestDropIsGreaterThanPiPercent_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetStrongBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrongBuyMovingAverageStatistics();
        
        var currentPrice = 100m;
        var latestBuyAfterFireSell = currentPrice - Convert.ToDecimal(double.Pi);

        // Act
        var result = sut.IsConditionsMet(
            oscillatorStatistics,
            movingAverageStatistics,
            90,
            Convert.ToDecimal(100.0 + double.Pi + 0.000001),
            currentPrice,
            latestBuyAfterFireSell);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True);
            Assert.That(sut.TradeAdvice, Is.EqualTo(TradeAdvice.PanicBuy2k));
        }

    }

    /// <summary>
    /// Regression: After fix, null lowestBuyAfterFireSell means "no fire-sell history" →
    /// the increase condition is considered met. PanicBuy must fire when indicator conditions are satisfied.
    /// </summary>
    [Test]
    public void IsConditionsMet_WhenLowestBuyAfterFireSellIsNull_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetStrongBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrongBuyMovingAverageStatistics();
        
        // Act
        var result = sut.IsConditionsMet(
            oscillatorStatistics,
            movingAverageStatistics,
            90,
            Convert.ToDecimal(100.0 + double.Pi + 0.000001),
            100,
            null);

        // Assert: null lowestBuyAfterFireSell means condition is met (no fire-sell history → PanicBuy allowed)
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsConditionsMet_WhenCurrentPriceIsNull_ShouldReturnFalse()
    {
        // Arrange
        var oscillatorStatistics = GetStrongBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrongBuyMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(
            oscillatorStatistics,
            movingAverageStatistics,
            90,
            Convert.ToDecimal(100.0 + double.Pi + 0.000001),
            null,
            95);

        // Assert
        Assert.That(result, Is.False);
    }

    /// <summary>
    /// Regression: After fix, null latestSellPrice means "no sell history" → price-drop condition
    /// is considered met. PanicBuy must fire when indicator conditions are satisfied.
    /// </summary>
    [Test]
    public void IsConditionsMet_WhenLatestSellPriceIsNull_ShouldReturnTrue()
    {
        // Arrange
        var oscillatorStatistics = GetStrongBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrongBuyMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(
            oscillatorStatistics,
            movingAverageStatistics,
            90,
            null,
            100,
            95);

        // Assert: null sell price means condition is met (no sell history → assume price drop is ok)
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsConditionsMet_WhenHighestPanicBuyPriceIsNotNull_ShouldStillCheckOtherConditions()
    {
        // Arrange
        var oscillatorStatistics = GetStrongBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrongBuyMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(
            oscillatorStatistics,
            movingAverageStatistics,
            null,
            Convert.ToDecimal(100.0 + double.Pi + 0.000001),
            100,
            90,
            Convert.ToDecimal(90 + double.Pi + 0.0001));

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True); // Nur latestBuyPrice ist null, andere Bedingungen sind erfüllt
            Assert.That(sut.TradeAdvice, Is.EqualTo(TradeAdvice.PanicBuy2k));
        }
    }

    [Test]
    public void IsConditionsMet_WhenHighestPanicBuyPriceIsBelowThreshold_ShouldStillCheckOtherConditions()
    {
        // Arrange
        var oscillatorStatistics = GetStrongBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrongBuyMovingAverageStatistics();

        var threePercent = 1m - (3m / 100m);

        // Act
        var result = sut.IsConditionsMet(
            oscillatorStatistics,
            movingAverageStatistics,
            null,
            Convert.ToDecimal(100.0 + double.Pi + 0.000001),
            100,
            90,
            100m * threePercent);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.False);
        }
    }

    [Test]
    public void IsConditionsMet_WhenLatestBuyPriceIsNull_ShouldStillCheckOtherConditions()
    {
        // Arrange
        var oscillatorStatistics = GetStrongBuyOscillatorStatistics();
        var movingAverageStatistics = GetStrongBuyMovingAverageStatistics();

        // Act
        var result = sut.IsConditionsMet(
            oscillatorStatistics,
            movingAverageStatistics,
            null,
            Convert.ToDecimal(100.0 + double.Pi + 0.000001),
            100,
            95);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result, Is.True); // Nur latestBuyPrice ist null, andere Bedingungen sind erfüllt
            Assert.That(sut.TradeAdvice, Is.EqualTo(TradeAdvice.PanicBuy2k));
        }
    }

    [Test]
    public void GetPercentage_WhenCurrentIsNull_ShouldReturnNull()
    {
        // Act
        var result = PercentageService.GetPercentage(null, 100);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetPercentage_WhenMaximumIsNull_ShouldReturnNull()
    {
        // Act
        var result = PercentageService.GetPercentage(50, null);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetPercentage_WhenMaximumIsZero_ShouldReturnNull()
    {
        // Act
        var result = PercentageService.GetPercentage(50, 0);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetPercentage_WhenCurrentIsZero_ShouldReturnZero()
    {
        // Act
        var result = PercentageService.GetPercentage(0, 100);

        // Assert
        Assert.That(result, Is.Zero);
    }

    [Test]
    public void GetPercentage_WhenValuesAreValid_ShouldReturnCorrectPercentage()
    {
        double current = 50;
        decimal maximum = 200;

        // Act
        var result = PercentageService.GetPercentage(current, Convert.ToDouble(maximum));

        // Assert
        Assert.That(result, Is.EqualTo(25.0)); // 50/200 * 100 = 25%
    }

    private static Dictionary<string, BitcoinTimeRangeStatistics> GetStrongBuyOscillatorStatistics() => new()
        {
            { "5m", BuildBitcoinTimeRangeStatistics(0, 1, false, true) },
            { "15m", BuildBitcoinTimeRangeStatistics(0, 2, true) },
            { "30m", BuildBitcoinTimeRangeStatistics(0, 2, false, true) },
            { "1h", BuildBitcoinTimeRangeStatistics(0, 3, true) },
            { "2h", BuildBitcoinTimeRangeStatistics(0, 3, false, true) },
        };

    private static Dictionary<string, BitcoinTimeRangeStatistics> GetStrongBuyMovingAverageStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(4, 2) },
        { "15m", BuildBitcoinTimeRangeStatistics(6, 1) },
        { "30m", BuildBitcoinTimeRangeStatistics(6, 1) },
        { "1h", BuildBitcoinTimeRangeStatistics(6, 1) },
        { "2h", BuildBitcoinTimeRangeStatistics(7, 0) },
    };

    // [TestCase(/*oscillator*//*5m*/0,2, /*Trend*/1,0,0,0,/*15m*/0,3, /*Trend*/1,0,0,0,/*30m*/0,1, /*Trend*/0,0,0,1,/*1h*/0,2, /*Trend*/1,0,0,0,/*2h*/0,0, /*Trend*/1,0,0,0,
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetStrictBuyOscillatorStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(0, 2, true) },
        { "15m", BuildBitcoinTimeRangeStatistics(0, 3, true) },
        { "30m", BuildBitcoinTimeRangeStatistics(0, 1) },
        { "1h", BuildBitcoinTimeRangeStatistics(0, 2, true) },
        { "2h", BuildBitcoinTimeRangeStatistics(0, 0, true) },
    };

    // /*movingaverage*//*5m*/6,1, /*Trend*/0,0,0,0,/*15m*/5,2, /*Trend*/0,0,0,0,/*30m*/3,3, /*Trend*/0,0,0,0,/*1h*/2,4, /*Trend*/0,0,0,0,/*2h*/2,5, /*Trend*/0,0,0,0,"StrictBuy")]
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetStrictBuyMovingAverageStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(6, 1) },
        { "15m", BuildBitcoinTimeRangeStatistics(5, 2) },
        { "30m", BuildBitcoinTimeRangeStatistics(3, 3) },
        { "1h", BuildBitcoinTimeRangeStatistics(2, 4) },
        { "2h", BuildBitcoinTimeRangeStatistics(2, 5) },
    };

    // [TestCase(/*5m*/0,1, /*Trend*/1,0,0,0,/*15m*/0,0, /*Trend*/0,0,0,1,/*30m*/0,1, /*Trend*/0,0,0,1,/*1h*/0,3, /*Trend*/0,0,0,1,/*2h*/0,3, /*Trend*/1,0,0,0,"Buy2k")]
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetBuy2kOscillatorStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(0, 1, true) },
        { "15m", BuildBitcoinTimeRangeStatistics(0, 0) },
        { "30m", BuildBitcoinTimeRangeStatistics(0, 1) },
        { "1h", BuildBitcoinTimeRangeStatistics(0, 3) },
        { "2h", BuildBitcoinTimeRangeStatistics(0, 3, true) },
    };

    // /*movingaverage*//*5m*/6,0, /*Trend*/0,0,0,0,/*15m*/7,0, /*Trend*/0,0,0,0,/*30m*/6,1, /*Trend*/0,0,0,0,/*1h*/4,2, /*Trend*/0,0,0,0,/*2h*/5,2, /*Trend*/0,0,0,0,"Buy2k")]
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetBuy2kMovingAverageStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(6, 0) },
        { "15m", BuildBitcoinTimeRangeStatistics(7, 0) },
        { "30m", BuildBitcoinTimeRangeStatistics(6, 1) },
        { "1h", BuildBitcoinTimeRangeStatistics(4, 2) },
        { "2h", BuildBitcoinTimeRangeStatistics(5, 2) },
    };

    // [TestCase(/*oscillator*//*5m*/0,1, /*Trend*/0,0,0,1,/*15m*/0,1, /*Trend*/0,0,0,1,/*30m*/1,0, /*Trend*/0,0,0,1,/*1h*/2,1, /*Trend*/0,0,0,1,/*2h*/1,1, /*Trend*/0,0,0,1,
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetBuyFormulaOscillatorStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(0, 1) },
        { "15m", BuildBitcoinTimeRangeStatistics(0, 1) },
        { "30m", BuildBitcoinTimeRangeStatistics(1, 0) },
        { "1h", BuildBitcoinTimeRangeStatistics(2, 1) },
        { "2h", BuildBitcoinTimeRangeStatistics(1, 1) },
    };

    // /*movingaverage*//*5m*/0,7, /*Trend*/0,0,0,0,/*15m*/1,6, /*Trend*/0,0,0,0,/*30m*/1,6, /*Trend*/0,0,0,0,/*1h*/0,6, /*Trend*/0,0,0,0,/*2h*/2,4, /*Trend*/0,0,0,0,"Buy")]
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetBuyFormulaMovingAverageStatistics() => new()
    {
        { "5m", BuildBitcoinTimeRangeStatistics(0, 7) },
        { "15m", BuildBitcoinTimeRangeStatistics(1, 6) },
        { "30m", BuildBitcoinTimeRangeStatistics(1, 6) },
        { "1h", BuildBitcoinTimeRangeStatistics(0, 6) },
        { "2h", BuildBitcoinTimeRangeStatistics(2, 4) },
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
            IndicatorBuyCount = buyCount,
            IndicatorSellCount = sellCount,
            IndicatorStrongTrendCount = isStrongTrend ? 1 : 0,
            IndicatorMediumTrendCount = isMediumTrend ? 1 : 0,
            IndicatorWeakTrendCount = isWeakTrend ? 1 : 0
        };
    }
}
