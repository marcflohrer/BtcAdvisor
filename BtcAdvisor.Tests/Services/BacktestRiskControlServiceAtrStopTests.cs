using BtcAdvisor.Services;

namespace BtcAdvisor.Tests.Services;

[TestFixture]
public class BacktestRiskControlServiceAtrStopTests
{
    private BacktestRiskControlService _sut = null!;

    [SetUp]
    public void Setup() => _sut = new BacktestRiskControlService();

    [Test]
    public void ShouldTriggerAtrStop_WhenPriceFallsBelowAtrStopLevel_Triggers()
    {
        // entry=100, ATR=2, k=2 → stop=96. Price 95.99 → trigger.
        Assert.That(_sut.ShouldTriggerAtrStop(100m, 95.99m, 2.0d, 2.0d), Is.True);
    }

    [Test]
    public void ShouldTriggerAtrStop_WhenPriceAtExactStopLevel_Triggers()
    {
        Assert.That(_sut.ShouldTriggerAtrStop(100m, 96m, 2.0d, 2.0d), Is.True);
    }

    [Test]
    public void ShouldTriggerAtrStop_WhenPriceAboveStopLevel_DoesNotTrigger()
    {
        Assert.That(_sut.ShouldTriggerAtrStop(100m, 97m, 2.0d, 2.0d), Is.False);
    }

    [Test]
    public void ShouldTriggerAtrStop_WhenAtrIsNull_ReturnsFalse()
    {
        Assert.That(_sut.ShouldTriggerAtrStop(100m, 50m, null, 2.0d), Is.False);
    }

    [Test]
    public void ShouldTriggerAtrStop_WhenAtrZeroOrNegative_ReturnsFalse()
    {
        Assert.That(_sut.ShouldTriggerAtrStop(100m, 50m, 0d, 2.0d), Is.False);
        Assert.That(_sut.ShouldTriggerAtrStop(100m, 50m, -1d, 2.0d), Is.False);
    }

    [Test]
    public void ShouldTriggerAtrStop_WhenMultiplierZeroOrNegative_ReturnsFalse()
    {
        Assert.That(_sut.ShouldTriggerAtrStop(100m, 50m, 2d, 0d), Is.False);
        Assert.That(_sut.ShouldTriggerAtrStop(100m, 50m, 2d, -1d), Is.False);
    }

    [Test]
    public void ShouldTriggerAtrStop_WhenEntryZeroOrNegative_ReturnsFalse()
    {
        Assert.That(_sut.ShouldTriggerAtrStop(0m, 50m, 2d, 2d), Is.False);
        Assert.That(_sut.ShouldTriggerAtrStop(-1m, 50m, 2d, 2d), Is.False);
    }

    [Test]
    public void ShouldTriggerAtrStop_FiresEarlierThanHardStop_WhenAtrTighter()
    {
        // entry=100, hard=-8% → 92; ATR=2, k=2 → 96. Price=94 → ATR triggers, hard not.
        Assert.That(_sut.ShouldTriggerAtrStop(100m, 94m, 2.0d, 2.0d), Is.True);
        Assert.That(_sut.ShouldTriggerHardStopLoss(100m, 94m, -8.0d), Is.False);
    }
}
