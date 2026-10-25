using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Indicators;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests.Services;

/// <summary>
/// Regression tests for StochasticFastIndicator.
/// Bug: after BaseIndicator.GetMaps fix, indicatorMapPrevious can be null when Previous is null.
/// StochasticFastIndicator dereferenced it without a null check (indicatorMapPrevious!.GetValue(...))
/// → NullReferenceException instead of graceful Signal.None.
/// Fix: added null check before accessing indicatorMapPrevious.
/// </summary>
[TestFixture]
public class StochasticFastIndicatorTests
{
    private StochasticFastIndicator _sut = null!;

    [SetUp]
    public void Setup()
    {
        _sut = new StochasticFastIndicator();
    }

    /// <summary>
    /// Regression: when Previous is null, must return Signal.None (not throw NullReferenceException).
    /// </summary>
    [Test]
    public void Evaluate_WhenPreviousIsNull_ShouldReturnNone()
    {
        var input = new IndicatorEvaluationInput
        {
            Price = 50000,
            Current = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_stochf_14_0", CreateDatum(fastK: 70, fastD: 60) },
            },
            Previous = null!
        };

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.None));
    }

    /// <summary>
    /// Buy signal: %K crosses above %D (previousK ≤ previousD, currentK > currentD).
    /// </summary>
    [Test]
    public void Evaluate_WhenKCrossesAboveD_ShouldReturnBuy()
    {
        var input = CreateInput(
            currentK: 70, currentD: 60,   // K > D now
            previousK: 50, previousD: 55  // K ≤ D before → crossover
        );

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Buy));
    }

    /// <summary>
    /// Sell signal: %K crosses below %D (previousK ≥ previousD, currentK < currentD).
    /// </summary>
    [Test]
    public void Evaluate_WhenKCrossesBelowD_ShouldReturnSell()
    {
        var input = CreateInput(
            currentK: 50, currentD: 60,   // K < D now
            previousK: 65, previousD: 60  // K ≥ D before → crossover
        );

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
            currentK: 70, currentD: 60,   // K > D now
            previousK: 65, previousD: 60  // K > D before → no crossover
        );

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Neutral));
    }

    private static IndicatorEvaluationInput CreateInput(
        double currentK, double currentD, double previousK, double previousD)
        => new IndicatorEvaluationInput
        {
            Price = 50000,
            Current = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_stochf_14_0", CreateDatum(currentK, currentD) },
            },
            Previous = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_stochf_14_0", CreateDatum(previousK, previousD) },
            }
        };

    private static IndicatorInputDatum CreateDatum(double fastK, double fastD)
    {
        var datum = new IndicatorInputDatum();
        datum.AddOrUpdateValue("valueFastK", fastK);
        datum.AddOrUpdateValue("valueFastD", fastD);
        return datum;
    }
}
