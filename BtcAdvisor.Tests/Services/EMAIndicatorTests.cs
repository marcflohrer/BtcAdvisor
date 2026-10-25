using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Indicators;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests.Services;

/// <summary>
/// Regression tests for EMAIndicator.
/// Bug: The second TryGetValue call used `currentIndicatorKey` (long-term) instead of
///      `shortTermIndicator` → both longTerm and shortTerm were always the same value
///      → EMA crossover never returned Buy or Sell, always Neutral.
/// Fix: Second TryGetValue now uses `shortTermIndicator` as the key.
/// </summary>
[TestFixture]
public class EMAIndicatorTests
{
    private EMAIndicator _sut = null!;

    [SetUp]
    public void Setup()
    {
        _sut = new EMAIndicator();
    }

    /// <summary>
    /// Regression: short-term EMA > long-term EMA must return Buy signal.
    /// Before fix: both reads used the same key → longTerm == shortTerm → Neutral.
    /// </summary>
    [Test]
    public void Evaluate_WhenShortTermEmaAboveLongTermEma_ShouldReturnBuy()
    {
        // EMA 20 (long) = 50000, EMA 10 (short) = 51000 → shortTerm > longTerm → Buy
        var input = CreateInput(period: 20, longTermValue: 50000, shortTermValue: 51000);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Buy));
    }

    /// <summary>
    /// Regression: short-term EMA &lt; long-term EMA must return Sell signal.
    /// Before fix: both reads used the same key → longTerm == shortTerm → Neutral.
    /// </summary>
    [Test]
    public void Evaluate_WhenShortTermEmaBelowLongTermEma_ShouldReturnSell()
    {
        // EMA 20 (long) = 50000, EMA 10 (short) = 48000 → shortTerm < longTerm → Sell
        var input = CreateInput(period: 20, longTermValue: 50000, shortTermValue: 48000);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Sell));
    }

    [Test]
    public void Evaluate_WhenShortTermEmaEqualsLongTermEma_ShouldReturnNeutral()
    {
        var input = CreateInput(period: 20, longTermValue: 50000, shortTermValue: 50000);

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.Neutral));
    }

    [Test]
    public void Evaluate_WhenCurrentIsNull_ShouldReturnNone()
    {
        var input = new IndicatorEvaluationInput { Current = null!, Previous = [] };

        var result = _sut.Evaluate(input);

        Assert.That(result, Is.EqualTo(Signal.None));
    }

    // Constructs an IndicatorEvaluationInput with 5m EMA keys for the given period (long) and
    // the corresponding short-term period derived via EmaMapToShortTermIndicator.
    // Period 20 → long key = ema_20, short key = ema_10
    // Period 50 → long key = ema_50, short key = ema_20
    private static IndicatorEvaluationInput CreateInput(int period, double longTermValue, double shortTermValue)
    {
        var longKey  = $"binance_BTC/USDT_5m_ema_{period}_0";
        var shortPeriod = period switch { 20 => 10, 50 => 20, 100 => 50, 200 => 100, _ => 10 };
        var shortKey = $"binance_BTC/USDT_5m_ema_{shortPeriod}_0";

        return new IndicatorEvaluationInput
        {
            Period = period,
            Current = new Dictionary<string, IndicatorInputDatum>
            {
                { longKey,  CreateDatum(longTermValue) },
                { shortKey, CreateDatum(shortTermValue) },
            }
        };
    }

    private static IndicatorInputDatum CreateDatum(double value)
    {
        var datum = new IndicatorInputDatum();
        datum.AddOrUpdateValue("value", value);
        return datum;
    }
}
