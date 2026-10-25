using Moq;
using BtcAdvisor.Repository;
using BtcAdvisor.Services;

namespace BtcAdvisor.Tests.Services;

[TestFixture]
public class GapDetectionServiceTests
{
    [Test]
    public async Task DetectCandleGapsAsync_ReturnsMissingTimestampsAndRanges()
    {
        var history = new Mock<IHistoricalBinanceCandleService>(MockBehavior.Strict);
        var stockRepository = new Mock<IStockCandleRepository>(MockBehavior.Strict);
        var bitcoinRepository = new Mock<IBitcoinRepository>(MockBehavior.Strict);
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedCandles = new[]
        {
            new OhlcvCandle(start, 1, 1, 1, 1, 1, start.AddHours(2)),
            new OhlcvCandle(start.AddHours(2), 1, 1, 1, 1, 1, start.AddHours(4)),
            new OhlcvCandle(start.AddHours(4), 1, 1, 1, 1, 1, start.AddHours(6)),
            new OhlcvCandle(start.AddHours(6), 1, 1, 1, 1, 1, start.AddHours(8)),
        };

        history
            .Setup(x => x.GetCandlesRangeAsync("2h", start, start.AddHours(8), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCandles);
        stockRepository
            .Setup(x => x.GetDistinctTimestampsAsync("BTC2H", start, start.AddHours(8), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { start.AddHours(2), start.AddHours(8) });

        var sut = new GapDetectionService(history.Object, stockRepository.Object, bitcoinRepository.Object);

        var result = await sut.DetectCandleGapsAsync("2h", start, start.AddHours(8), CancellationToken.None);

        Assert.That(result.MissingTimestampsUtc, Is.EqualTo(new[] { start.AddHours(4), start.AddHours(6) }));
        Assert.That(result.MissingRanges, Has.Count.EqualTo(1));
        Assert.That(result.MissingRanges[0].MissingCount, Is.EqualTo(2));
    }

    [Test]
    public async Task DetectIndicatorGapsAsync_FiltersByPeriodMinutes()
    {
        var history = new Mock<IHistoricalBinanceCandleService>(MockBehavior.Strict);
        var stockRepository = new Mock<IStockCandleRepository>(MockBehavior.Strict);
        var bitcoinRepository = new Mock<IBitcoinRepository>(MockBehavior.Strict);
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedCandles = new[]
        {
            new OhlcvCandle(start, 1, 1, 1, 1, 1, start.AddHours(2)),
            new OhlcvCandle(start.AddHours(2), 1, 1, 1, 1, 1, start.AddHours(4)),
            new OhlcvCandle(start.AddHours(4), 1, 1, 1, 1, 1, start.AddHours(6)),
        };

        history
            .Setup(x => x.GetCandlesRangeAsync("2h", start, start.AddHours(6), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCandles);
        bitcoinRepository
            .Setup(x => x.GetDistinctTimestampsAsync(start, start.AddHours(6), 120, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { start.AddHours(2), start.AddHours(6) });

        var sut = new GapDetectionService(history.Object, stockRepository.Object, bitcoinRepository.Object);

        var result = await sut.DetectIndicatorGapsAsync("2h", start, start.AddHours(6), CancellationToken.None);

        Assert.That(result.MissingTimestampsUtc, Is.EqualTo(new[] { start.AddHours(4) }));
        bitcoinRepository.VerifyAll();
    }
}