using BtcAdvisor.Services;

namespace BtcAdvisor.Tests.Services;

[TestFixture]
public class BacktestRiskControlServiceEvaluateStopTests
{
    private BacktestRiskControlService _sut = null!;

    [SetUp]
    public void Setup() => _sut = new BacktestRiskControlService();

    [Test]
    public void EvaluateHardStop_WhenCloseBelowThreshold_TriggersAtThresholdPriceMinusSlippage()
    {
        // entry=100, threshold=-15% → thresholdPrice=85, close=84 (crosses), slippage=0.5% → fill=84.575
        var eval = _sut.EvaluateHardStop(100m, currentPrice: 84m, barLow: null, thresholdPercent: -15d, slippagePercent: 0.5d);

        Assert.That(eval.Triggered, Is.True);
        Assert.That(eval.ExitPrice, Is.EqualTo(84.575m).Within(0.001m));
    }

    [Test]
    public void EvaluateHardStop_WhenCloseAboveThreshold_DoesNotTrigger()
    {
        // entry=100, threshold=-15% → thresholdPrice=85, close=90 → no trigger
        var eval = _sut.EvaluateHardStop(100m, currentPrice: 90m, barLow: 85m, thresholdPercent: -15d, slippagePercent: 0.5d);

        Assert.That(eval.Triggered, Is.False);
    }

    [Test]
    public void EvaluateHardStop_WhenCloseWellBelowThreshold_FillsAtThresholdNotAtClose()
    {
        // Crash: close=60 (well below 85), fill still at threshold × (1-slippage) = 84.575 — NOT at 60.
        var eval = _sut.EvaluateHardStop(100m, currentPrice: 60m, barLow: null, thresholdPercent: -15d, slippagePercent: 0.5d);

        Assert.That(eval.Triggered, Is.True);
        Assert.That(eval.ExitPrice, Is.EqualTo(84.575m).Within(0.001m));
    }

    [Test]
    public void EvaluateHardStop_WhenEntryZero_DoesNotTrigger()
    {
        var eval = _sut.EvaluateHardStop(0m, currentPrice: 50m, barLow: 40m, thresholdPercent: -15d, slippagePercent: 0.5d);

        Assert.That(eval.Triggered, Is.False);
    }

    [Test]
    public void EvaluateAtrStop_WhenCloseReachesAtrLevel_TriggersAtAtrLevelMinusSlippage()
    {
        // entry=100, atr=2, k=2 → stopLevel=96, close=95.9 (crosses), slippage=0.5% → fill=95.52
        var eval = _sut.EvaluateAtrStop(100m, currentPrice: 95.9m, barLow: null, atrValue: 2d, atrMultiplier: 2d, slippagePercent: 0.5d);

        Assert.That(eval.Triggered, Is.True);
        Assert.That(eval.ExitPrice, Is.EqualTo(95.52m).Within(0.001m));
    }

    [Test]
    public void EvaluateAtrStop_WhenAtrNullOrZero_DoesNotTrigger()
    {
        Assert.That(_sut.EvaluateAtrStop(100m, 50m, 40m, null, 2d, 0.5d).Triggered, Is.False);
        Assert.That(_sut.EvaluateAtrStop(100m, 50m, 40m, 0d, 2d, 0.5d).Triggered, Is.False);
    }

    [Test]
    public void EvaluateAtrStop_WhenBarLowAboveStop_DoesNotTrigger()
    {
        var eval = _sut.EvaluateAtrStop(100m, currentPrice: 98m, barLow: 97m, atrValue: 2d, atrMultiplier: 2d, slippagePercent: 0.5d);

        Assert.That(eval.Triggered, Is.False);
    }
}
