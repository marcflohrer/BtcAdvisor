using Microsoft.Extensions.Caching.Memory;
using Moq;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class MarketRegimeServiceTests
{
    private Mock<IBinanceKlineService> _mockBinanceKlineService = null!;
    private MarketRegimeService _sut = null!;

    [SetUp]
    public void Setup()
    {
        _mockBinanceKlineService = new Mock<IBinanceKlineService>(MockBehavior.Strict);
        _sut = new MarketRegimeService(
            _mockBinanceKlineService.Object,
            new LocalIndicatorComputationService(),
            new MemoryCache(new MemoryCacheOptions()));
    }

    [Test]
    public async Task GetRegimeAsync_WhenDailyTrendIsStrongBull_ReturnsBull()
    {
        var asOf = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SetupDailyCandles(BuildCandles(asOf, 260, 100, 1.2));

        var result = await _sut.GetRegimeAsync(asOf, CancellationToken.None);

        Assert.That(result.Regime, Is.EqualTo(MarketRegime.Bull));
    }

    [Test]
    public async Task GetRegimeAsync_WhenDailyTrendIsStrongBear_ReturnsBear()
    {
        var asOf = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SetupDailyCandles(BuildCandles(asOf, 260, 400, -1.1));

        var result = await _sut.GetRegimeAsync(asOf, CancellationToken.None);

        Assert.That(result.Regime, Is.EqualTo(MarketRegime.Bear));
    }

    [Test]
    public async Task GetRegimeAsync_WhenAdxIsWeak_ReturnsNeutral()
    {
        var asOf = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SetupDailyCandles(BuildFlatCandles(asOf, 260, 200));

        var result = await _sut.GetRegimeAsync(asOf, CancellationToken.None);

        Assert.That(result.Regime, Is.EqualTo(MarketRegime.Neutral));
    }

    private void SetupDailyCandles(IReadOnlyList<OhlcvCandle> candles)
    {
        _mockBinanceKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
    }

    private static IReadOnlyList<OhlcvCandle> BuildCandles(DateTime asOf, int count, double startClose, double dailyDelta)
    {
        var candles = new List<OhlcvCandle>(count);
        var first = asOf.Date.AddDays(-(count - 1));

        for (var index = 0; index < count; index++)
        {
            var close = startClose + (dailyDelta * index);
            var open = close - (dailyDelta * 0.5);
            var high = Math.Max(open, close) + 3;
            var low = Math.Min(open, close) - 3;
            var closeTime = first.AddDays(index);

            candles.Add(new OhlcvCandle(closeTime.AddDays(-1), open, high, low, close, 1_000, closeTime));
        }

        return candles.AsReadOnly();
    }

    private static IReadOnlyList<OhlcvCandle> BuildFlatCandles(DateTime asOf, int count, double center)
    {
        var candles = new List<OhlcvCandle>(count);
        var first = asOf.Date.AddDays(-(count - 1));

        for (var index = 0; index < count; index++)
        {
            var wiggle = (index % 2 == 0 ? 1 : -1) * 0.4;
            var close = center + wiggle;
            var open = center - wiggle;
            var high = center + 1;
            var low = center - 1;
            var closeTime = first.AddDays(index);

            candles.Add(new OhlcvCandle(closeTime.AddDays(-1), open, high, low, close, 1_000, closeTime));
        }

        return candles.AsReadOnly();
    }
}