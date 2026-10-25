using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Indicators;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests.Services;

/// <summary>
/// Regression tests for KickingIndicator.
/// Bug: missing NaN check for currentKicking (value). When the API returns null,
/// GetValue returns NaN. NaN == 100 and NaN == -100 are both false, causing the
/// indicator to return Signal.Neutral instead of Signal.None. This inflates the
/// neutral count and misclassifies missing data as "no kicking pattern found".
/// Fix: added NaN check after reading currentKicking.
/// </summary>
[TestFixture]
public class KickingIndicatorTests
{
    private KickingIndicator _sut = null!;

    [SetUp]
    public void Setup()
    {
        _sut = new KickingIndicator();
    }

    /// <summary>
    /// Regression: when currentKicking is NaN, must return Signal.None (not Neutral).
    /// </summary>
    [Test]
    public void Evaluate_WhenValueIsNaN_ShouldReturnNone()
    {
        var input = CreateInput(double.NaN);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.None));
    }

    /// <summary>
    /// Bullish kicking pattern (value == 100) → Buy.
    /// </summary>
    [Test]
    public void Evaluate_WhenValueIs100_ShouldReturnBuy()
    {
        var input = CreateInput(100);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Buy));
    }

    /// <summary>
    /// Bearish kicking pattern (value == -100) → Sell.
    /// </summary>
    [Test]
    public void Evaluate_WhenValueIsMinus100_ShouldReturnSell()
    {
        var input = CreateInput(-100);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Sell));
    }

    /// <summary>
    /// No kicking pattern (value == 0) → Neutral.
    /// </summary>
    [Test]
    public void Evaluate_WhenValueIsZero_ShouldReturnNeutral()
    {
        var input = CreateInput(0);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Neutral));
    }

    private static IndicatorEvaluationInput CreateInput(double kickingValue)
        => new()
        {
            Price = 50000,
            Current = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_kickingbylength_0_0", CreateDatum(kickingValue) },
            },
            Previous = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_kickingbylength_0_0", CreateDatum(0) },
            }
        };

    private static IndicatorInputDatum CreateDatum(double value)
    {
        var datum = new IndicatorInputDatum();
        datum.AddOrUpdateValue("value", value);
        return datum;
    }
}
