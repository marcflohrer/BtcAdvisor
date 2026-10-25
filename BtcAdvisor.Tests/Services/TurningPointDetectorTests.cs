using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;

namespace BtcAdvisor.Tests.Services;

[TestFixture]
public class TurningPointDetectorTests
{
    [Test]
    public void DetectByThirtyDayWindow_FindsLocalHighAndLow()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<StockCandle>();

        for (var i = 0; i < 90; i++)
        {
            candles.Add(new StockCandle
            {
                Symbol = "BTC",
                Timestamp = start.AddDays(i),
                ClosePrice = 100m
            });
        }

        // Clear local high with 30 days before/after context.
        candles[40].ClosePrice = 200m;
        // Clear local low with 30 days before/after context.
        candles[50].ClosePrice = 10m;

        var result = TurningPointDetector.DetectByThirtyDayWindow(candles);

        Assert.That(result, Has.Some.Matches<TurningPoint>(x => x.Kind == TurningPointKind.LocalHigh && x.Timestamp == candles[40].Timestamp));
        Assert.That(result, Has.Some.Matches<TurningPoint>(x => x.Kind == TurningPointKind.LocalLow && x.Timestamp == candles[50].Timestamp));
    }

    [Test]
    public void DetectByThirtyDayWindow_DoesNotClassifyEdgesWithoutContext()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = Enumerable.Range(0, 40)
            .Select(i => new StockCandle
            {
                Symbol = "BTC",
                Timestamp = start.AddDays(i),
                ClosePrice = 100m + i
            })
            .ToList();

        // Potential high too close to start: no full 30-day lookback context.
        candles[5].ClosePrice = 999m;

        var result = TurningPointDetector.DetectByThirtyDayWindow(candles);

        Assert.That(result.Any(x => x.Timestamp == candles[5].Timestamp), Is.False);
    }

    [Test]
    public void DetectByThirtyDayWindow_FiltersShallowLocalLowByDepth()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<StockCandle>();

        for (var i = 0; i < 90; i++)
        {
            candles.Add(new StockCandle
            {
                Symbol = "BTC",
                Timestamp = start.AddDays(i),
                ClosePrice = 100m
            });
        }

        // Local low candidate, but only 5% below neighborhood median.
        candles[45].ClosePrice = 95m;

        var result = TurningPointDetector.DetectByThirtyDayWindow(candles, minSwingFraction: 0.07, minDepthFraction: 0.07);

        Assert.That(result.Any(x => x.Timestamp == candles[45].Timestamp), Is.False);
    }

    [Test]
    public void DetectByThirtyDayWindow_KeepsDeeperLocalLowByDepth()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<StockCandle>();

        for (var i = 0; i < 90; i++)
        {
            candles.Add(new StockCandle
            {
                Symbol = "BTC",
                Timestamp = start.AddDays(i),
                ClosePrice = 100m
            });
        }

        // Local low candidate, 10% below neighborhood median.
        candles[45].ClosePrice = 90m;

        var result = TurningPointDetector.DetectByThirtyDayWindow(candles, minSwingFraction: 0.07, minDepthFraction: 0.07);

        Assert.That(result.Any(x => x.Timestamp == candles[45].Timestamp && x.Kind == TurningPointKind.LocalLow), Is.True);
    }

    [Test]
    public void DetectByWindow_UsesConfiguredWindowDays()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<StockCandle>();

        for (var i = 0; i < 100; i++)
        {
            candles.Add(new StockCandle
            {
                Symbol = "BTC",
                Timestamp = start.AddDays(i),
                ClosePrice = 100m
            });
        }

        candles[30].ClosePrice = 150m;
    candles[40].ClosePrice = 50m;
        candles[50].ClosePrice = 200m;

        var shortWindow = TurningPointDetector.DetectByWindow(candles, windowDays: 10);
        var longWindow = TurningPointDetector.DetectByWindow(candles, windowDays: 30);

        Assert.That(shortWindow.Any(x => x.Kind == TurningPointKind.LocalHigh && x.Timestamp == candles[30].Timestamp), Is.True);
        Assert.That(longWindow.Any(x => x.Kind == TurningPointKind.LocalHigh && x.Timestamp == candles[30].Timestamp), Is.False);
    }

    // --- ApplyMinimumSwingFilter ---

    [Test]
    public void ApplyMinimumSwingFilter_RemovesWeakHighBetweenTwoLows()
    {
        // Low(100) → High(105, +5%) → Low(90) : the High is only 5% above the preceding Low
        // → High should be removed, leaving Low(100) and Low(90).
        // Since two consecutive Lows remain, the higher one (100) is then also removed.
        var points = new List<TurningPoint>
        {
            new(new DateTime(2025,1,1, 0,0,0,DateTimeKind.Utc), 100m, TurningPointKind.LocalLow),
            new(new DateTime(2025,2,1, 0,0,0,DateTimeKind.Utc), 105m, TurningPointKind.LocalHigh),
            new(new DateTime(2025,3,1, 0,0,0,DateTimeKind.Utc),  90m, TurningPointKind.LocalLow),
        };

        var result = TurningPointDetector.ApplyMinimumSwingFilter(points, 0.07);

        // Only the most extreme Low (90) survives; the weak High and the higher Low are collapsed.
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ClosePrice, Is.EqualTo(90m));
        Assert.That(result[0].Kind, Is.EqualTo(TurningPointKind.LocalLow));
    }

    [Test]
    public void ApplyMinimumSwingFilter_KeepsSignificantSwings()
    {
        // Low(100) → High(115, +15%) → Low(90, -21.7%) — both swings > 7%
        var points = new List<TurningPoint>
        {
            new(new DateTime(2025,1,1, 0,0,0,DateTimeKind.Utc), 100m, TurningPointKind.LocalLow),
            new(new DateTime(2025,2,1, 0,0,0,DateTimeKind.Utc), 115m, TurningPointKind.LocalHigh),
            new(new DateTime(2025,3,1, 0,0,0,DateTimeKind.Utc),  90m, TurningPointKind.LocalLow),
        };

        var result = TurningPointDetector.ApplyMinimumSwingFilter(points, 0.07);

        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void ApplyMinimumSwingFilter_CollapsesTwoConsecutiveLows_KeepsMoreExtreme()
    {
        // Two Lows: 100 then 95 (5% apart). Should keep only the lower one (95).
        var points = new List<TurningPoint>
        {
            new(new DateTime(2025,1,1, 0,0,0,DateTimeKind.Utc), 100m, TurningPointKind.LocalLow),
            new(new DateTime(2025,2,1, 0,0,0,DateTimeKind.Utc),  95m, TurningPointKind.LocalLow),
        };

        var result = TurningPointDetector.ApplyMinimumSwingFilter(points, 0.07);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ClosePrice, Is.EqualTo(95m));
    }

    [Test]
    public void ApplyMinimumSwingFilter_CollapsesTwoConsecutiveHighs_KeepsMoreExtreme()
    {
        // Two Highs: 100 then 104 (4% apart). Should keep only the higher one (104).
        var points = new List<TurningPoint>
        {
            new(new DateTime(2025,1,1, 0,0,0,DateTimeKind.Utc), 100m, TurningPointKind.LocalHigh),
            new(new DateTime(2025,2,1, 0,0,0,DateTimeKind.Utc), 104m, TurningPointKind.LocalHigh),
        };

        var result = TurningPointDetector.ApplyMinimumSwingFilter(points, 0.07);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ClosePrice, Is.EqualTo(104m));
    }
}
