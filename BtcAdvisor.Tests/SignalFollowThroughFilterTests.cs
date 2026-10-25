using BtcAdvisor.Services;

namespace BtcAdvisor.Tests;

[TestFixture]
public class SignalFollowThroughFilterTests
{
    [Test]
    public void PassesBullish_WhenPriceDeclines_ReturnsFalse()
    {
        var result = SignalFollowThroughFilter.PassesBullish(99m, 100m, 0.0);

        Assert.That(result, Is.False);
    }

    [Test]
    public void PassesBullish_WhenRiseBelowThreshold_ReturnsFalse()
    {
        var result = SignalFollowThroughFilter.PassesBullish(100.4m, 100m, 0.5);

        Assert.That(result, Is.False);
    }

    [Test]
    public void PassesBearish_WhenPriceRises_ReturnsFalse()
    {
        var result = SignalFollowThroughFilter.PassesBearish(101m, 100m, 0.0);

        Assert.That(result, Is.False);
    }

    [Test]
    public void FilterOrdered_UsesPreviousAcceptedSignalAsAnchor()
    {
        var points = new[]
        {
            new SignalFollowThroughPoint(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100m),
            new SignalFollowThroughPoint(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 99m),
            new SignalFollowThroughPoint(new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), 100m),
        };

        var filtered = SignalFollowThroughFilter.FilterOrdered(points, isBullish: true, minMovePercent: 0.5);

        Assert.That(filtered.Select(x => x.Timestamp).ToArray(), Is.EqualTo(new[] { points[0].Timestamp }));
    }
}