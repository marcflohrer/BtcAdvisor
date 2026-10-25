using Microsoft.Extensions.Caching.Memory;
using Moq;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class BearMarketFilterServiceTests
{
    private Mock<IBinanceKlineService> _mockKlineService;
    private Mock<ILocalIndicatorComputationService> _mockIndicatorService;
    private IMemoryCache _memoryCache;
    private BearMarketFilterService _sut;

    private static readonly DateTime AsOf = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void Setup()
    {
        _mockKlineService = new Mock<IBinanceKlineService>(MockBehavior.Strict);
        _mockIndicatorService = new Mock<ILocalIndicatorComputationService>(MockBehavior.Strict);
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _sut = new BearMarketFilterService(_mockKlineService.Object, _mockIndicatorService.Object, _memoryCache);
    }

    [TearDown]
    public void TearDown()
    {
        _memoryCache.Dispose();
    }

    private static List<OhlcvCandle> MakeCandles(IEnumerable<double> closes)
    {
        var list = closes.ToList();
        return list
            .Select((c, i) => new OhlcvCandle(
                AsOf.AddDays(-(list.Count - 1 - i)),
                c,
                c,
                c,
                c,
                0d,
                AsOf.AddDays(-(list.Count - 1 - i))))
            .ToList();
    }

    private static List<OhlcvCandle> MakeCandlesWithRanges(IEnumerable<(double High, double Low, double Close)> rows)
    {
        var list = rows.ToList();
        return list
            .Select((row, i) => new OhlcvCandle(
                AsOf.AddDays(-(list.Count - 1 - i)),
                row.Close,
                row.High,
                row.Low,
                row.Close,
                0d,
                AsOf.AddDays(-(list.Count - 1 - i))))
            .ToList();
    }

    [Test]
    public async Task IsBearMarketAsync_PriceBelowSma_ReturnsTrue()
    {
        var candles = MakeCandles(Enumerable.Range(0, 200).Select(i => 100.0 - i * 0.1));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 200))
            .Returns(Enumerable.Repeat<double?>(null, 199).Append(95.0).ToArray());

        var result = await _sut.IsBearMarketAsync("1d", AsOf, 200, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsBearMarketAsync_PriceAboveSma_ReturnsFalse()
    {
        var candles = MakeCandles(Enumerable.Range(0, 200).Select(i => 100.0 + i * 0.1));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 200))
            .Returns(Enumerable.Repeat<double?>(null, 199).Append(105.0).ToArray());

        var result = await _sut.IsBearMarketAsync("1d", AsOf, 200, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsBearMarketAsync_InsufficientHistory_ReturnsFalseFailOpen()
    {
        var candles = MakeCandles(Enumerable.Range(0, 50).Select(i => 100.0));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var result = await _sut.IsBearMarketAsync("1d", AsOf, 200, CancellationToken.None);

        Assert.That(result, Is.False);
        _mockIndicatorService.Verify(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task IsBearMarketAsync_CachesResult_SecondCallSkipsKlineService()
    {
        var candles = MakeCandles(Enumerable.Range(0, 200).Select(i => 100.0));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 200))
            .Returns(Enumerable.Repeat<double?>(null, 199).Append(101.0).ToArray());

        var first = await _sut.IsBearMarketAsync("1d", AsOf, 200, CancellationToken.None);
        var second = await _sut.IsBearMarketAsync("1d", AsOf, 200, CancellationToken.None);

        Assert.That(first, Is.True);
        Assert.That(second, Is.True);
        _mockKlineService.Verify(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- IsDeepCycleLowAsync ---

    [Test]
    public async Task IsDeepCycleLowAsync_DrawdownExceedsThreshold_ReturnsTrue()
    {
        // ATH = 100, latest = 15 → -85 % drawdown, threshold = 75 %
        var candles = MakeCandles([100.0, 80.0, 50.0, 30.0, 15.0]);
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var result = await _sut.IsDeepCycleLowAsync("1d", AsOf, 75.0, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsDeepCycleLowAsync_DrawdownBelowThreshold_ReturnsFalse()
    {
        // ATH = 100, latest = 55 → -45 % drawdown, threshold = 75 %
        var candles = MakeCandles([100.0, 80.0, 70.0, 60.0, 55.0]);
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var result = await _sut.IsDeepCycleLowAsync("1d", AsOf, 75.0, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsDeepCycleLowAsync_ExactlyAtThreshold_ReturnsTrue()
    {
        // ATH = 100, latest = 25 → exactly -75 % drawdown, threshold = 75 %
        var candles = MakeCandles([100.0, 25.0]);
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var result = await _sut.IsDeepCycleLowAsync("1d", AsOf, 75.0, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsDeepCycleLowAsync_UsesLatestLow_NotLatestClose()
    {
        // ATH high = 100, latest low = 24 (-76%), latest close = 40 (-60%).
        // Low-based logic must trigger deep-cycle-low.
        var candles = MakeCandlesWithRanges([
            (100.0, 90.0, 95.0),
            (90.0, 24.0, 40.0)
        ]);

        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var result = await _sut.IsDeepCycleLowAsync("1d", AsOf, 75.0, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsDeepCycleLowAsync_InsufficientHistory_ReturnsFalseFailOpen()
    {
        var candles = MakeCandles([100.0]);
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var result = await _sut.IsDeepCycleLowAsync("1d", AsOf, 75.0, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsDeepCycleLowAsync_CachesResult_SecondCallSkipsKlineService()
    {
        var candles = MakeCandles([100.0, 80.0, 50.0, 30.0, 15.0]);
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var first = await _sut.IsDeepCycleLowAsync("1d", AsOf, 75.0, CancellationToken.None);
        var second = await _sut.IsDeepCycleLowAsync("1d", AsOf, 75.0, CancellationToken.None);

        Assert.That(first, Is.True);
        Assert.That(second, Is.True);
        _mockKlineService.Verify(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void IsDeepCycleLowAsync_InvalidThreshold_Throws()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.IsDeepCycleLowAsync("1d", AsOf, 0.0, CancellationToken.None));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.IsDeepCycleLowAsync("1d", AsOf, 100.0, CancellationToken.None));
    }

    [Test]
    public async Task IsCycleTopOvervaluedAsync_WhenNearAthAndStrongDrawup_ReturnsTrue()
    {
        var candles = MakeCandlesWithRanges([
            (120.0, 100.0, 115.0),
            (260.0, 90.0, 245.0),
            (420.0, 80.0, 410.0)
        ]);

        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var result = await _sut.IsCycleTopOvervaluedAsync(
            "1d",
            AsOf,
            lookbackDays: 1460,
            minDrawupFromCycleLowPercent: 350.0,
            minAthProximityPercent: 90.0,
            CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsCycleTopOvervaluedAsync_WhenFarFromAth_ReturnsFalse()
    {
        var candles = MakeCandlesWithRanges([
            (420.0, 100.0, 390.0),
            (320.0, 110.0, 300.0),
            (300.0, 120.0, 280.0)
        ]);

        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var result = await _sut.IsCycleTopOvervaluedAsync(
            "1d",
            AsOf,
            lookbackDays: 1460,
            minDrawupFromCycleLowPercent: 150.0,
            minAthProximityPercent: 90.0,
            CancellationToken.None);

        Assert.That(result, Is.False);
    }

    // --- IsPiCycleTopAsync ---

    private static double?[] MakeSmaResult(int length, double? lastValue)
    {
        var arr = new double?[length];
        arr[^1] = lastValue;
        return arr;
    }

    [Test]
    public async Task IsPiCycleTopAsync_Sma111EqualsDoubleSma350_ReturnsTrue()
    {
        // 350 candles with close=100
        var candles = MakeCandles(Enumerable.Repeat(100.0, 350));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
        // SMA(111) = 200, SMA(350) = 100 → 200 >= 100 * 2 → true
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 111))
            .Returns(MakeSmaResult(350, 200.0));
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 350))
            .Returns(MakeSmaResult(350, 100.0));

        var result = await _sut.IsPiCycleTopAsync("1d", AsOf, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsPiCycleTopAsync_Sma111BelowDoubleSma350_ReturnsFalse()
    {
        var candles = MakeCandles(Enumerable.Repeat(100.0, 350));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
        // SMA(111) = 190, SMA(350) = 100 → 190 < 200 → false
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 111))
            .Returns(MakeSmaResult(350, 190.0));
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 350))
            .Returns(MakeSmaResult(350, 100.0));

        var result = await _sut.IsPiCycleTopAsync("1d", AsOf, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsPiCycleTopAsync_InsufficientHistory_ReturnsFalseFailOpen()
    {
        // Only 349 candles — below the 350 minimum
        var candles = MakeCandles(Enumerable.Repeat(100.0, 349));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var result = await _sut.IsPiCycleTopAsync("1d", AsOf, CancellationToken.None);

        Assert.That(result, Is.False);
        _mockIndicatorService.Verify(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task IsPiCycleTopAsync_CachesResult_SecondCallSkipsKlineService()
    {
        var candles = MakeCandles(Enumerable.Repeat(100.0, 350));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 111))
            .Returns(MakeSmaResult(350, 200.0));
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 350))
            .Returns(MakeSmaResult(350, 100.0));

        var first = await _sut.IsPiCycleTopAsync("1d", AsOf, CancellationToken.None);
        var second = await _sut.IsPiCycleTopAsync("1d", AsOf, CancellationToken.None);

        Assert.That(first, Is.True);
        Assert.That(second, Is.True);
        _mockKlineService.Verify(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void IsPiCycleTopAsync_EmptyInterval_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(
            () => _sut.IsPiCycleTopAsync("", AsOf, CancellationToken.None));
    }

    // --- IsPuellMultipleUndervaluedAsync ---

    [Test]
    public async Task IsPuellMultipleUndervalued_PriceFarBelowSma365_ReturnsTrue()
    {
        // 365 candles, last close = 50, SMA(365) mocked at 200 → puell = 0.25 < 0.5 → true
        var candles = MakeCandles(Enumerable.Repeat(100.0, 365));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 365))
            .Returns(MakeSmaResult(365, 200.0));
        // Override last close to 50 via a custom candle list
        candles[^1] = new OhlcvCandle(AsOf, 50, 50, 50, 50, 0, AsOf);

        var result = await _sut.IsPuellMultipleUndervaluedAsync("1d", AsOf, 0.5, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsPuellMultipleUndervalued_PriceAboveSma365_ReturnsFalse()
    {
        // 365 candles, close = SMA → puell = 1.0 → above 0.5 → false
        var candles = MakeCandles(Enumerable.Repeat(100.0, 365));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 365))
            .Returns(MakeSmaResult(365, 100.0));

        var result = await _sut.IsPuellMultipleUndervaluedAsync("1d", AsOf, 0.5, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsPuellMultipleUndervalued_InsufficientHistory_ReturnsFalseFailOpen()
    {
        // Only 364 candles — below the 365 minimum
        var candles = MakeCandles(Enumerable.Repeat(100.0, 364));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);

        var result = await _sut.IsPuellMultipleUndervaluedAsync("1d", AsOf, 0.5, CancellationToken.None);

        Assert.That(result, Is.False);
        _mockIndicatorService.Verify(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task IsPuellMultipleUndervalued_CachesResult_SecondCallSkipsKlineService()
    {
        var candles = MakeCandles(Enumerable.Repeat(100.0, 365));
        _mockKlineService
            .Setup(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candles);
        _mockIndicatorService
            .Setup(i => i.ComputeSma(It.IsAny<IReadOnlyList<double>>(), 365))
            .Returns(MakeSmaResult(365, 300.0)); // puell = 100/300 ≈ 0.33 < 0.5

        var first = await _sut.IsPuellMultipleUndervaluedAsync("1d", AsOf, 0.5, CancellationToken.None);
        var second = await _sut.IsPuellMultipleUndervaluedAsync("1d", AsOf, 0.5, CancellationToken.None);

        Assert.That(first, Is.True);
        Assert.That(second, Is.True);
        _mockKlineService.Verify(s => s.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void IsPuellMultipleUndervalued_EmptyInterval_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(
            () => _sut.IsPuellMultipleUndervaluedAsync("", AsOf, 0.5, CancellationToken.None));
    }

    [Test]
    public void IsPuellMultipleUndervalued_ZeroThreshold_Throws()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.IsPuellMultipleUndervaluedAsync("1d", AsOf, 0.0, CancellationToken.None));
    }

    // ── IsNearAthForSellSuppressAsync ──────────────────────────────────────────

    [Test]
    public async Task IsNearAthForSellSuppressAsync_WhenPriceAtAth_ReturnsTrue()
    {
        // ATH = 100, latest close = 100 → proximity = 100% ≥ 80% → suppressed
        _mockKlineService
            .Setup(k => k.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCandles([80.0, 90.0, 100.0]));

        var result = await _sut.IsNearAthForSellSuppressAsync("1d", AsOf, 80.0, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsNearAthForSellSuppressAsync_WhenPriceBelowMinPercent_ReturnsFalse()
    {
        // ATH = 100, latest close = 70 → proximity = 70% < 80% → not suppressed
        _mockKlineService
            .Setup(k => k.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCandles([100.0, 90.0, 70.0]));

        var result = await _sut.IsNearAthForSellSuppressAsync("1d", AsOf, 80.0, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsNearAthForSellSuppressAsync_WhenExactlyAtThreshold_ReturnsTrue()
    {
        // ATH = 100, latest close = 80, minPercent = 80 → 80% >= 80% → suppressed
        _mockKlineService
            .Setup(k => k.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCandles([100.0, 90.0, 80.0]));

        var result = await _sut.IsNearAthForSellSuppressAsync("1d", AsOf, 80.0, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsNearAthForSellSuppressAsync_WhenInsufficientHistory_ReturnsFalse()
    {
        // Only 1 candle — fail-open (do not suppress)
        _mockKlineService
            .Setup(k => k.GetCandlesUpToAsync("1d", AsOf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCandles([100.0]));

        var result = await _sut.IsNearAthForSellSuppressAsync("1d", AsOf, 80.0, CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsNearAthForSellSuppressAsync_WhenMinPercentZero_Throws()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.IsNearAthForSellSuppressAsync("1d", AsOf, 0.0, CancellationToken.None));
    }

    [Test]
    public void IsNearAthForSellSuppressAsync_WhenMinPercentAbove100_Throws()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.IsNearAthForSellSuppressAsync("1d", AsOf, 101.0, CancellationToken.None));
    }

    [Test]
    public void IsNearAthForSellSuppressAsync_WhenIntervalEmpty_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(
            () => _sut.IsNearAthForSellSuppressAsync("", AsOf, 80.0, CancellationToken.None));
    }
}
