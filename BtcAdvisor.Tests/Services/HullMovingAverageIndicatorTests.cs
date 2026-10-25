using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Indicators;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests.Services;

/// <summary>
/// Regression tests for HullMovingAverageIndicator.
/// Bug: `previousHMA` was read from `indicatorMapCurrent` (the current map) instead of
///      `indicatorMapPrevious` → currentHMA always equalled previousHMA
///      → crossover comparison was meaningless; signals were based only on price vs current HMA.
/// Fix: `indicatorMapPrevious` is now correctly populated from the Previous dictionary.
/// </summary>
[TestFixture]
public class HullMovingAverageIndicatorTests
{
    private HullMovingAverageIndicator _sut = null!;

    [SetUp]
    public void Setup()
    {
        _sut = new HullMovingAverageIndicator();
    }

    /// <summary>
    /// Regression: previousHMA must be read from the Previous map, not Current.
    /// When price is above both currentHMA and previousHMA → Buy.
    /// </summary>
    [Test]
    public void Evaluate_WhenPriceAboveBothCurrentAndPreviousHma_ShouldReturnBuy()
    {
        // currentHMA = 48000, previousHMA = 47000 (from separate Previous dict)
        // price = 50000 > both → Buy
        var input = CreateInput(price: 50000, currentHma: 48000, previousHma: 47000);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Buy));
    }

    /// <summary>
    /// Regression: previousHMA must be read from the Previous map.
    /// When price is below both currentHMA and previousHMA → Sell.
    /// </summary>
    [Test]
    public void Evaluate_WhenPriceBelowBothCurrentAndPreviousHma_ShouldReturnSell()
    {
        // currentHMA = 52000, previousHMA = 51000
        // price = 50000 < both → Sell
        var input = CreateInput(price: 50000, currentHma: 52000, previousHma: 51000);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Sell));
    }

    /// <summary>
    /// When price is between currentHMA and previousHMA, the trend is ambiguous → Neutral.
    /// </summary>
    [Test]
    public void Evaluate_WhenPriceBetweenCurrentAndPreviousHma_ShouldReturnNeutral()
    {
        // price 50000 > currentHMA 48000 but price 50000 < previousHMA 51000 → Neutral
        var input = CreateInput(price: 50000, currentHma: 48000, previousHma: 51000);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Neutral));
    }

    /// <summary>
    /// Regression for BaseIndicator.GetMaps fix: when Previous == null, GetMaps now returns
    /// indicatorMapPrevious = null instead of throwing NullReferenceException.
    /// The null guard in HullMovingAverageIndicator is now reachable and returns Signal.None.
    /// </summary>
    [Test]
    public void Evaluate_WhenPreviousIsNull_ShouldReturnNone()
    {
        var input = new IndicatorEvaluationInput
        {
            Price = 50000,
            Current = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_hma_9_0", CreateDatum(48000) },
            },
            Previous = null!
        };

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.None));
    }

    private static IndicatorEvaluationInput CreateInput(double price, double currentHma, double previousHma)
        => new IndicatorEvaluationInput
        {
            Price = price,
            Current = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_hma_9_0", CreateDatum(currentHma) },
            },
            Previous = new Dictionary<string, IndicatorInputDatum>
            {
                { "binance_BTC/USDT_5m_hma_9_0", CreateDatum(previousHma) },
            }
        };

    private static IndicatorInputDatum CreateDatum(double value)
    {
        var datum = new IndicatorInputDatum();
        datum.AddOrUpdateValue("value", value);
        return datum;
    }
}
