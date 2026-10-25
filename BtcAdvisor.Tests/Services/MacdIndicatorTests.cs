using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Indicators;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests.Services;

/// <summary>
/// Regression tests for MacdIndicator.
/// Bug: missing NaN check for currentSignalLine (valueMacdsignal). When the API returns null
/// for the signal line, GetValue returns NaN. Without a NaN check, both crossover comparisons
/// (currentMacd > NaN and currentMacd &lt; NaN) silently evaluate to false, causing the indicator
/// to return Signal.Neutral instead of Signal.None. This inflates the neutral count and
/// misclassifies missing data as a valid "no crossover" reading.
/// Fix: added NaN check after reading currentSignalLine.
/// </summary>
[TestFixture]
public class MacdIndicatorTests
{
    private MacdIndicator _sut = null!;

    [SetUp]
    public void Setup()
    {
        _sut = new MacdIndicator();
    }

    /// <summary>
    /// Regression: when currentSignalLine is NaN, must return Signal.None (not Neutral).
    /// </summary>
    [Test]
    public void Evaluate_WhenCurrentSignalLineIsNaN_ShouldReturnNone()
    {
        var input = CreateInput(
            currentMacd: 100, currentSignal: double.NaN,
            previousMacd: 80, previousSignal: 90);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.None));
    }

    /// <summary>
    /// Sanity: when currentMacd is NaN, must return Signal.None.
    /// </summary>
    [Test]
    public void Evaluate_WhenCurrentMacdIsNaN_ShouldReturnNone()
    {
        var input = CreateInput(
            currentMacd: double.NaN, currentSignal: 90,
            previousMacd: 80, previousSignal: 90);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.None));
    }

    /// <summary>
    /// Buy signal: MACD crosses signal line from below (previousMacd ≤ previousSignal, currentMacd > currentSignal).
    /// </summary>
    [Test]
    public void Evaluate_WhenMacdCrossesAboveSignalLine_ShouldReturnBuy()
    {
        var input = CreateInput(
            currentMacd: 100, currentSignal: 90,
            previousMacd: 80, previousSignal: 85);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Buy));
    }

    /// <summary>
    /// Sell signal: MACD crosses signal line from above (previousMacd ≥ previousSignal, currentMacd &lt; currentSignal).
    /// </summary>
    [Test]
    public void Evaluate_WhenMacdCrossesBelowSignalLine_ShouldReturnSell()
    {
        var input = CreateInput(
            currentMacd: 80, currentSignal: 90,
            previousMacd: 95, previousSignal: 90);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Sell));
    }

    /// <summary>
    /// No crossover → Neutral.
    /// </summary>
    [Test]
    public void Evaluate_WhenNoCrossover_ShouldReturnNeutral()
    {
        var input = CreateInput(
            currentMacd: 100, currentSignal: 90,
            previousMacd: 95, previousSignal: 90);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Neutral));
    }

    /// <summary>
    /// When Previous is null, must return Signal.None.
    /// </summary>
    [Test]
    public void Evaluate_WhenPreviousIsNull_ShouldReturnNone()
    {
        var input = new IndicatorEvaluationInput
        {
            Price = 50000,
            Current = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_macd_12_0", CreateDatum(100, 90) },
            },
            Previous = null!
        };

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.None));
    }

    private static IndicatorEvaluationInput CreateInput(
        double currentMacd, double currentSignal,
        double previousMacd, double previousSignal)
        => new()
        {
            Price = 50000,
            Current = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_macd_12_0", CreateDatum(currentMacd, currentSignal) },
            },
            Previous = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_macd_12_0", CreateDatum(previousMacd, previousSignal) },
            }
        };

    private static IndicatorInputDatum CreateDatum(double macd, double signal)
    {
        var datum = new IndicatorInputDatum();
        datum.AddOrUpdateValue("valueMacd", macd);
        datum.AddOrUpdateValue("valueMacdsignal", signal);
        return datum;
    }
}
