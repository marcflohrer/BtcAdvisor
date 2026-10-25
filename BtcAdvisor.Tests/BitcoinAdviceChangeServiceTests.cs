using Microsoft.Extensions.Options;
using Moq;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class BitcoinAdviceChangeServiceTests
{
    private Mock<ITradingRepository> _tradingRepository = null!;
    private Mock<ITradeOpportunityService> _tradeOpportunityService = null!;
    private Mock<IBacktestRiskControlService> _riskControlService = null!;
    private Mock<IAtrLookupService> _atrLookupService = null!;
    private Mock<IBinanceKlineService> _binanceKlineService = null!;

    [SetUp]
    public void Setup()
    {
        _tradingRepository = new Mock<ITradingRepository>(MockBehavior.Strict);
        _tradeOpportunityService = new Mock<ITradeOpportunityService>(MockBehavior.Strict);
        _riskControlService = new Mock<IBacktestRiskControlService>(MockBehavior.Strict);
        _atrLookupService = new Mock<IAtrLookupService>(MockBehavior.Strict);
        _binanceKlineService = new Mock<IBinanceKlineService>(MockBehavior.Strict);
    }

    private BitcoinAdviceChangeService BuildSut(RiskControlOptions options) =>
        new(_tradingRepository.Object,
            _tradeOpportunityService.Object,
            _riskControlService.Object,
            Options.Create(options),
            _atrLookupService.Object,
            _binanceKlineService.Object);

    private void SetupCurrentPrice(DateTime dateTime, double price) =>
        _binanceKlineService
            .Setup(k => k.GetCandlesUpToAsync("1h", dateTime, CancellationToken.None))
            .ReturnsAsync(new List<OhlcvCandle>
            {
                new(dateTime.AddHours(-1), price, price, price, price, 1d, dateTime)
            });

    [Test]
    public async Task SendChangeAlertsToBot_WhenHardStopLossTriggered_ShouldSendHardStopLossDealAndReturnEarly()
    {
        var now = new DateTime(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);
        var latestBuy = new BuyActionLog { TradeAdvice = "PanicBuy", Price = 100_000m, TradingDate = now.AddHours(-2) };

        _tradingRepository.Setup(r => r.FindLatestAction(now)).ReturnsAsync((null, latestBuy));
        _tradingRepository.Setup(r => r.FindLatestSellAction(now)).ReturnsAsync((SellActionLog?)null);

        SetupCurrentPrice(now, 84_000d);

        // entry=100_000, threshold=-15% → thresholdPrice=85_000, with slippage 0.5% → exit=84_575
        _riskControlService
            .Setup(r => r.EvaluateHardStop(latestBuy.Price, 84_000m, (decimal?)null, -15d, 0.5d))
            .Returns(new StopLossEvaluation(true, 84_575m));

        _tradeOpportunityService
            .Setup(s => s.TradeOpportunity(
                It.Is<Deal>(d => d.TradeAdvice == "HardStopLoss" && d.Price == 84_575m && d.TradingDate == now),
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        await BuildSut(new RiskControlOptions { EnableHardStopLoss = true, HardStopLossPercent = -15d })
            .SendChangeAlertsToBot(now, CancellationToken.None);

        _tradeOpportunityService.VerifyAll();
        // Trailing stop candles must NOT be fetched — early return after hard stop
        _binanceKlineService.Verify(k => k.GetCandlesRangeAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SendChangeAlertsToBot_WhenNoPriceTrigger_ShouldPassMacroOnlyDealWithBinancePrice()
    {
        var now = new DateTime(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);
        var latestBuy = new BuyActionLog { TradeAdvice = "PanicBuy", Price = 100_000m, TradingDate = now.AddHours(-2) };
        // Sell is more recent than buy → no open position → trailing/breakeven block skipped
        var latestSell = new SellActionLog { Price = 110_000m, TradingDate = now.AddHours(-1) };

        _tradingRepository.Setup(r => r.FindLatestAction(now)).ReturnsAsync((latestSell, latestBuy));
        _tradingRepository.Setup(r => r.FindLatestSellAction(now)).ReturnsAsync(latestSell);

        SetupCurrentPrice(now, 95_000d);

        _tradeOpportunityService
            .Setup(s => s.TradeOpportunity(
                It.Is<Deal>(d => d.TradeAdvice == "MacroOnly" && d.Price == 95_000m && d.TradingDate == now),
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        await BuildSut(new RiskControlOptions { EnableHardStopLoss = false })
            .SendChangeAlertsToBot(now, CancellationToken.None);

        _tradeOpportunityService.VerifyAll();
        _binanceKlineService.Verify(k => k.GetCandlesRangeAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SendChangeAlertsToBot_WhenTrailingStopTriggered_ShouldSendTrailingStopDealAndReturnEarly()
    {
        var entryDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var latestBuy = new BuyActionLog { TradeAdvice = "PanicBuy", Price = 50_000m, TradingDate = entryDate };

        _tradingRepository.Setup(r => r.FindLatestAction(now)).ReturnsAsync((null, latestBuy));
        _tradingRepository.Setup(r => r.FindLatestSellAction(now)).ReturnsAsync((SellActionLog?)null);

        SetupCurrentPrice(now, 57_000d);

        var mockCandles = new List<OhlcvCandle>
        {
            new(entryDate, 50_000d, 100_000d, 48_000d, 98_000d, 1000d, entryDate.AddHours(23)),
            new(entryDate.AddDays(1), 95_000d, 97_000d, 55_000d, 57_000d, 800d, entryDate.AddDays(1).AddHours(23))
        };
        _binanceKlineService
            .Setup(k => k.GetCandlesRangeAsync("1d", entryDate, now, CancellationToken.None))
            .ReturnsAsync(mockCandles);

        _riskControlService
            .Setup(r => r.EvaluateTrailingStop(50_000m, 57_000m, 100_000m, 100.0, 40.0, 0.5))
            .Returns(new StopLossEvaluation(true, 59_700m));

        _tradeOpportunityService
            .Setup(s => s.TradeOpportunity(
                It.Is<Deal>(d =>
                    d.TradeAdvice == "HardStopLoss" &&
                    d.Price == 59_700m &&
                    d.DebugString.Contains("(trigger=TrailingStop)", StringComparison.Ordinal)),
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        await BuildSut(new RiskControlOptions
        {
            EnableHardStopLoss = false,
            EnableTrailingStop = true,
            TrailingStopActivationPercent = 100.0,
            TrailingStopPercent = 40.0,
            StopLossSlippagePercent = 0.5
        }).SendChangeAlertsToBot(now, CancellationToken.None);

        _tradeOpportunityService.VerifyAll();
    }

    [Test]
    public async Task SendChangeAlertsToBot_WhenBreakevenStopTriggered_ShouldSendBreakevenStopDealAndReturnEarly()
    {
        var entryDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2024, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var latestBuy = new BuyActionLog { TradeAdvice = "PanicBuy", Price = 50_000m, TradingDate = entryDate };

        _tradingRepository.Setup(r => r.FindLatestAction(now)).ReturnsAsync((null, latestBuy));
        _tradingRepository.Setup(r => r.FindLatestSellAction(now)).ReturnsAsync((SellActionLog?)null);

        SetupCurrentPrice(now, 49_800d);

        // One candle with close >= 15% above entry → breakevenActive = true; current 49_800 <= 50_000 → fires
        var mockCandles = new List<OhlcvCandle>
        {
            new(entryDate, 50_000d, 62_000d, 49_000d, 60_000d, 1000d, entryDate.AddHours(23)),
            new(entryDate.AddDays(1), 58_000d, 59_000d, 48_000d, 49_800d, 900d, entryDate.AddDays(1).AddHours(23))
        };
        _binanceKlineService
            .Setup(k => k.GetCandlesRangeAsync("1d", entryDate, now, CancellationToken.None))
            .ReturnsAsync(mockCandles);

        _riskControlService
            .Setup(r => r.EvaluateHardStop(50_000m, 49_800m, (decimal?)null, 0.0, 0.5))
            .Returns(new StopLossEvaluation(true, 49_551m));

        _tradeOpportunityService
            .Setup(s => s.TradeOpportunity(
                It.Is<Deal>(d =>
                    d.TradeAdvice == "HardStopLoss" &&
                    d.Price == 49_551m &&
                    d.DebugString.Contains("(trigger=BreakevenStop)", StringComparison.Ordinal)),
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        await BuildSut(new RiskControlOptions
        {
            EnableHardStopLoss = false,
            EnableTrailingStop = false,
            BreakevenTriggerPercent = 15.0,
            StopLossSlippagePercent = 0.5
        }).SendChangeAlertsToBot(now, CancellationToken.None);

        _tradeOpportunityService.VerifyAll();
    }

    [Test]
    public async Task SendChangeAlertsToBot_WhenNoOpenPosition_ShouldSkipTrailingAndBreakevenBlock()
    {
        var now = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var latestBuy = new BuyActionLog { Price = 50_000m, TradingDate = now.AddDays(-30) };
        // Sell is more recent → no open position
        var latestSell = new SellActionLog { Price = 60_000m, TradingDate = now.AddDays(-5) };

        _tradingRepository.Setup(r => r.FindLatestAction(now)).ReturnsAsync((latestSell, latestBuy));
        _tradingRepository.Setup(r => r.FindLatestSellAction(now)).ReturnsAsync(latestSell);

        SetupCurrentPrice(now, 65_000d);

        _tradeOpportunityService
            .Setup(s => s.TradeOpportunity(
                It.Is<Deal>(d => d.TradeAdvice == "MacroOnly" && d.Price == 65_000m),
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        await BuildSut(new RiskControlOptions
        {
            EnableHardStopLoss = false,
            EnableTrailingStop = true,
            BreakevenTriggerPercent = 15.0
        }).SendChangeAlertsToBot(now, CancellationToken.None);

        // 1d candles must NOT be fetched when no open position
        _binanceKlineService.Verify(k => k.GetCandlesRangeAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _tradeOpportunityService.VerifyAll();
    }
}
