using Moq;
using Microsoft.Extensions.Options;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;
using static BtcAdvisor.Extensions.DateTimeExtensions;

namespace BtcAdvisor.Tests;

[TestFixture]
public class TradeOpportunityServiceTests
{
    private Mock<ITradingRepository> _mockTradingRepository;
    private Mock<ITelegramBotService> _mockTelegramBotService;
    private Mock<IActionLogService> _mockActionLogService;
    private Mock<IBinanceKlineService> _mockBinanceKlineService;
    private TradeOpportunityService _tradeOpportunityService;

    [SetUp]
    public void Setup()
    {
        _mockTradingRepository = new Mock<ITradingRepository>(MockBehavior.Strict);
        _mockTelegramBotService = new Mock<ITelegramBotService>(MockBehavior.Strict);
        _mockActionLogService = new Mock<IActionLogService>(MockBehavior.Strict);
        _mockBinanceKlineService = new Mock<IBinanceKlineService>(MockBehavior.Strict);
        _mockBinanceKlineService
            .Setup(b => b.GetCandlesUpToAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        _tradeOpportunityService = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions { MacroOnlyMode = true }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));
    }

    [Test]
    public async Task TradeOpportunity_WhenPanicBuyAfterFireSell_ShouldSendBuyMessageAndSaveBuyLog()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "PanicBuy", Price = 100 };
        var latestFireSell = new SellActionLog { TradingDate = utcDate.AddMinutes(-30), TradeAdvice = "FireSell", Price = 95 };
        var latestPanicBuy = new BuyActionLog { TradingDate = utcDate.AddMinutes(-60), TradeAdvice = "PanicBuy", Price = 90 };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestFireSell, latestPanicBuy));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(It.Is<BuyActionLog>(b => b.TradeAdvice == "StrongBuy" && b.Price == 100), CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearFilter = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearFilter.Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object, _mockTelegramBotService.Object, _mockActionLogService.Object,
            _mockBinanceKlineService.Object, bearFilter.Object,
            Options.Create(new BearMarketFilterOptions { MacroOnlyMode = true, AthDrawdownOverrideEnabled = true }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        // Act
        await sut.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.Is<BuyActionLog>(b => b.TradeAdvice == "StrongBuy" && b.Price == 100), CancellationToken.None), Times.Once);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Once);
        _mockActionLogService.Verify(s => s.SaveAsync(It.Is<ActionLog>(a => a.Price == 100 && a.TradeAdvice == "StrongBuy" && a.TradingDate.CompareTo(utcDate) == 0)), Times.Once);
    }

    [Test]
    public async Task TradeOpportunity_WhenFireSellAfterPanicBuy_ShouldSendSellMessageAndSaveSellLog()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "FireSellStrong", Price = 105 };
        var latestFireSell = new SellActionLog { TradingDate = utcDate.AddMinutes(-60), TradeAdvice = "FireSellStrong", Price = 110 };
        var latestPanicBuy = new BuyActionLog { TradingDate = utcDate.AddMinutes(-30), TradeAdvice = "PanicBuy", Price = 95 };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestFireSell, latestPanicBuy));
        _mockTradingRepository.Setup(r => r.SaveSellActionLog(It.Is<SellActionLog>(s => s.TradeAdvice == "DistributionSell" && s.Price == 105), CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearFilter = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearFilter.Setup(b => b.IsCycleTopOvervaluedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object, _mockTelegramBotService.Object, _mockActionLogService.Object,
            _mockBinanceKlineService.Object, bearFilter.Object,
            Options.Create(new BearMarketFilterOptions { MacroOnlyMode = true, CycleTopSellZoneEnabled = true }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        // Act
        await sut.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.Is<SellActionLog>(s => s.TradeAdvice == "DistributionSell" && s.Price == 105), CancellationToken.None), Times.Once);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Once);
        _mockActionLogService.Verify(s => s.SaveAsync(It.Is<ActionLog>(a => a.Price == 105 && a.TradeAdvice == "DistributionSell" && a.TradingDate.CompareTo(utcDate) == 0)), Times.Once);
    }

    [Test]
    public async Task TradeOpportunity_WhenPanicBuyAfterPanicBuy_ShouldNotSendBuyMessageAndShouldNotSaveBuyLog()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "PanicBuy", Price = 100 };
        var latestFireSell = new SellActionLog { TradingDate = utcDate.AddMinutes(-60), TradeAdvice = "FireSell", Price = 95 };
        var latestPanicBuy = new BuyActionLog { TradingDate = utcDate.AddMinutes(-30), TradeAdvice = "PanicBuy", Price = 90 };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestFireSell, latestPanicBuy));
        // Act
        await _tradeOpportunityService.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), CancellationToken.None), Times.Never);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task TradeOpportunity_WhenFireSellAfterFireSell_ShouldNotSendSellMessageAndShouldNotSaveSellLog()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "FireSellStrong", Price = 105 };
        var latestFireSell = new SellActionLog { TradingDate = utcDate.AddMinutes(-30), TradeAdvice = "FireSellStrong", Price = 100 };
        var latestPanicBuy = new BuyActionLog { TradingDate = utcDate.AddMinutes(-60), TradeAdvice = "PanicBuy", Price = 95 };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestFireSell, latestPanicBuy));
        // Act
        await _tradeOpportunityService.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.IsAny<SellActionLog>(), CancellationToken.None), Times.Never);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task TradeOpportunity_WhenNormalBuy_ShouldSaveBuyLogAndSendMessage()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 100 };
        var latestFireSell = new SellActionLog { TradingDate = utcDate.AddMinutes(-30), TradeAdvice = "FireSell", Price = 95 };
        var latestPanicBuy = new BuyActionLog { TradingDate = utcDate.AddMinutes(-60), TradeAdvice = "PanicBuy", Price = 90 };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestFireSell, latestPanicBuy));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(It.Is<BuyActionLog>(b => b.TradeAdvice == "StrongBuy" && b.Price == 100), CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearFilter = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearFilter.Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object, _mockTelegramBotService.Object, _mockActionLogService.Object,
            _mockBinanceKlineService.Object, bearFilter.Object,
            Options.Create(new BearMarketFilterOptions { MacroOnlyMode = true, AthDrawdownOverrideEnabled = true }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        // Act
        await sut.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.Is<BuyActionLog>(b => b.TradeAdvice == "StrongBuy" && b.Price == 100), CancellationToken.None), Times.Once);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task TradeOpportunity_WhenNormalSell_ShouldSaveSellLogAndSendMessage()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Sell", Price = 105 };
        var latestFireSell = new SellActionLog { TradingDate = utcDate.AddMinutes(-60), TradeAdvice = "FireSell", Price = 110 };
        var latestPanicBuy = new BuyActionLog { TradingDate = utcDate.AddMinutes(-30), TradeAdvice = "PanicBuy", Price = 95 };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestFireSell, latestPanicBuy));
        _mockTradingRepository.Setup(r => r.SaveSellActionLog(It.Is<SellActionLog>(s => s.TradeAdvice == "DistributionSell" && s.Price == 105), CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearFilter = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearFilter.Setup(b => b.IsCycleTopOvervaluedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object, _mockTelegramBotService.Object, _mockActionLogService.Object,
            _mockBinanceKlineService.Object, bearFilter.Object,
            Options.Create(new BearMarketFilterOptions { MacroOnlyMode = true, CycleTopSellZoneEnabled = true }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        // Act
        await sut.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.Is<SellActionLog>(s => s.TradeAdvice == "DistributionSell" && s.Price == 105), CancellationToken.None), Times.Once);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task TradeOpportunity_WhenNoPreviousActions_ShouldSaveAndSendPanicBuy()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "PanicBuy", Price = 100 };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((null, null));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(It.Is<BuyActionLog>(b => b.TradeAdvice == "StrongBuy" && b.Price == 100), CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearFilter = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearFilter.Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object, _mockTelegramBotService.Object, _mockActionLogService.Object,
            _mockBinanceKlineService.Object, bearFilter.Object,
            Options.Create(new BearMarketFilterOptions { MacroOnlyMode = true, AthDrawdownOverrideEnabled = true }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        // Act
        await sut.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.Is<BuyActionLog>(b => b.TradeAdvice == "StrongBuy" && b.Price == 100), CancellationToken.None), Times.Once);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Once);
        _mockActionLogService.Verify(s => s.SaveAsync(It.Is<ActionLog>(a => a.Price == 100 && a.TradeAdvice == "StrongBuy" && a.TradingDate.CompareTo(utcDate) == 0)), Times.Once);
    }

    [Test]
    public async Task TradeOpportunity_WhenNoPreviousActions_ShouldNotSaveOrSendFireSell()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "FireSellStrong", Price = 105 };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((null, null));
        // Act
        await _tradeOpportunityService.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.IsAny<SellActionLog>(), CancellationToken.None), Times.Never);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Never);
        _mockActionLogService.Verify(s => s.SaveAsync(It.IsAny<ActionLog>()), Times.Never);
    }

    [Test]
    public async Task TradeOpportunity_WhenDuplicateBuyAction_ShouldNotSaveBuyLog()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "PanicBuy", Price = 100 };
        var latestFireSell = new SellActionLog { TradingDate = utcDate.AddMinutes(-30), TradeAdvice = "FireSellStrong", Price = 95 };
        var latestPanicBuy = new BuyActionLog { TradingDate = utcDate.AddMinutes(-60), TradeAdvice = "PanicBuy", Price = 90 };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestFireSell, latestPanicBuy));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(It.Is<BuyActionLog>(b => b.TradeAdvice == "StrongBuy" && b.Price == 100 && b.TradingDate == utcDate), CancellationToken.None))
            .Returns(Task.CompletedTask)
            .Verifiable("SaveBuyActionLog should be called, but repository will skip duplicate.");
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearFilter = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearFilter.Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object, _mockTelegramBotService.Object, _mockActionLogService.Object,
            _mockBinanceKlineService.Object, bearFilter.Object,
            Options.Create(new BearMarketFilterOptions { MacroOnlyMode = true, AthDrawdownOverrideEnabled = true }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        // Act
        await sut.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.Is<BuyActionLog>(b => b.TradeAdvice == "StrongBuy" && b.Price == 100 && b.TradingDate == utcDate), CancellationToken.None), Times.Once);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task TradeOpportunity_WhenDuplicateSellAction_ShouldNotSaveSellLog()
    {
        // Arrange
        // latestFireSell (-30min) ist neuer als latestPanicBuy (-60min):
        // latestActionWasRelevantSell = true → shouldSendSellMessage = false (kein erneuter Versand)
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "FireSellStrong", Price = 105 };
        var latestFireSell = new SellActionLog { TradingDate = utcDate.AddMinutes(-30), TradeAdvice = "FireSellStrong", Price = 100 };
        var latestPanicBuy = new BuyActionLog { TradingDate = utcDate.AddMinutes(-60), TradeAdvice = "PanicBuy", Price = 95 };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestFireSell, latestPanicBuy));
        // Act
        await _tradeOpportunityService.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.IsAny<SellActionLog>(), CancellationToken.None), Times.Never);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task TradeOpportunity_WhenHardStopLoss_WithExistingBuyPosition_ShouldSaveSellLogAndSendTelegramMessage()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var entryPrice = 100_000m;
        var currentPrice = 84_000m; // -16% → triggers hard stop at -15%
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "HardStopLoss", Price = currentPrice };
        var latestFireSell = new SellActionLog { TradingDate = utcDate.AddDays(-30), TradeAdvice = "FireSell", Price = 90_000m };
        var latestPanicBuy = new BuyActionLog { TradingDate = utcDate.AddDays(-10), TradeAdvice = "PanicBuy", Price = entryPrice };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestFireSell, latestPanicBuy));
        _mockTradingRepository.Setup(r => r.FindLatestFireSellAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync(latestFireSell);
        _mockTradingRepository.Setup(r => r.FindLatestPanicBuyAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync(latestPanicBuy);
        _mockTradingRepository.Setup(r => r.SaveSellActionLog(
                It.Is<SellActionLog>(s => s.TradeAdvice == "HardStopLoss" && s.Price == currentPrice),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        // Act
        await _tradeOpportunityService.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveSellActionLog(
            It.Is<SellActionLog>(s => s.TradeAdvice == "HardStopLoss" && s.Price == currentPrice),
            CancellationToken.None), Times.Once);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Once);
        _mockActionLogService.Verify(s => s.SaveAsync(It.Is<ActionLog>(a => a.TradeAdvice == "HardStopLoss" && a.Price == currentPrice)), Times.Once);
    }

    [Test]
    public async Task TradeOpportunity_WhenHardStopLoss_WithNoBuyPosition_ShouldNotSaveSellLogOrSendMessage()
    {
        // Arrange
        var utcDate = DateTime.UtcNow;
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "HardStopLoss", Price = 84_000m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((null, null));

        // Act
        await _tradeOpportunityService.TradeOpportunity(deal, CancellationToken.None);

        // Assert
        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.IsAny<SellActionLog>(), CancellationToken.None), Times.Never);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task TradeOpportunity_BuyAfterClosedCycle_PersistsEvenWhenBelowPreviousBuyPrice()
    {
        // Cross-cycle buy: position is closed (latestSell more recent than latestBuy).
        // Even at a lower price than the historical buy, the new entry must be accepted —
        // the follow-through anchor only governs intra-cluster signals, not cross-cycle entries.
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = utcDate.AddHours(-2), TradeAdvice = "FireSell", Price = 110m };
        var latestBuy = new BuyActionLog { TradingDate = utcDate.AddHours(-4), TradeAdvice = "PanicBuy", Price = 100m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 99m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, latestBuy));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(It.Is<BuyActionLog>(b => b.Price == 99m && b.TradeAdvice == "StrongBuy"), CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearFilter = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearFilter.Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object, _mockTelegramBotService.Object, _mockActionLogService.Object,
            _mockBinanceKlineService.Object, bearFilter.Object,
            Options.Create(new BearMarketFilterOptions { MacroOnlyMode = true, AthDrawdownOverrideEnabled = true }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.Is<BuyActionLog>(b => b.Price == 99m), CancellationToken.None), Times.Once);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task TradeOpportunity_SellWithOpenPosition_PersistsEvenWhenAbovePreviousSellPrice()
    {
        // Cross-cycle sell: position is open (latestBuy more recent than latestSell).
        // Even at a higher price than the historical sell, the exit must be accepted.
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = utcDate.AddHours(-4), TradeAdvice = "FireSellStrong", Price = 110m };
        var latestBuy = new BuyActionLog { TradingDate = utcDate.AddHours(-2), TradeAdvice = "PanicBuy", Price = 105m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Sell", Price = 112m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, latestBuy));
        _mockTradingRepository.Setup(r => r.SaveSellActionLog(It.Is<SellActionLog>(s => s.Price == 112m && s.TradeAdvice == "DistributionSell"), CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearFilter = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearFilter.Setup(b => b.IsCycleTopOvervaluedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object, _mockTelegramBotService.Object, _mockActionLogService.Object,
            _mockBinanceKlineService.Object, bearFilter.Object,
            Options.Create(new BearMarketFilterOptions { MacroOnlyMode = true, CycleTopSellZoneEnabled = true }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.Is<SellActionLog>(s => s.Price == 112m), CancellationToken.None), Times.Once);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Once);
    }

    private TradeOpportunityService BuildServiceWithBearFilter(
        bool suppressEnabled,
        bool isDeepCycleLow)
    {
        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(isDeepCycleLow);
        bearMarketFilterService
            .Setup(b => b.IsCycleTopOvervaluedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                CycleTopSellZoneEnabled = true,
                AthDrawdownSuppressBearishSellEnabled = suppressEnabled,
                AthDrawdownSuppressBearishSellThresholdPercent = 75.0
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));
    }

    [Test]
    public async Task HandleBuy_DeepCycleOverride_UsesNextCandleOpenInRealtime()
    {
        var utcDate = DateTime.UtcNow;
        var nextOpen = utcDate.AddHours(1);
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 100m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((null, null));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(
                It.Is<BuyActionLog>(b => b.Price == 95m && b.TradingDate == nextOpen.ToUtcDateTime()),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        _mockBinanceKlineService.Setup(b => b.GetCandlesUpToAsync("1h", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>
            {
                new(utcDate.AddMinutes(-30), 100, 101, 98, 99, 0, utcDate),
                new(nextOpen, 95, 99, 94, 97, 0, nextOpen.AddHours(1))
            });

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsBearMarketAsync("1h", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync("1h", utcDate.ToUtcDateTime(), 75.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                Enabled = true,
                Interval = "1h",
                MacroOnlyMode = true,
                AthDrawdownOverrideEnabled = true,
                AthDrawdownOverrideThresholdPercent = 75.0,
                AthDrawdownInterval = "1h"
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(
            It.Is<BuyActionLog>(b => b.Price == 95m && b.TradingDate == nextOpen.ToUtcDateTime()),
            CancellationToken.None), Times.Once);
    }

    [Test]
    public async Task HandleBuy_DeepCycleOverride_AdaptiveThreshold_UsesCycleBasedValue()
    {
        var utcDate = new DateTime(2026, 5, 9, 12, 0, 0, DateTimeKind.Utc);
        var nextOpen = utcDate.AddHours(1);
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 100m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((null, null));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(
                It.IsAny<BuyActionLog>(),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        _mockBinanceKlineService.Setup(b => b.GetCandlesUpToAsync("1h", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>
            {
                new(utcDate.AddMinutes(-30), 100, 101, 98, 99, 0, utcDate),
                new(nextOpen, 95, 99, 94, 97, 0, nextOpen.AddHours(1))
            });

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync("1h", utcDate.ToUtcDateTime(), 65.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                AthDrawdownOverrideEnabled = true,
                AthDrawdownOverrideThresholdPercent = 75.0,
                AthDrawdownInterval = "1h",
                AthDrawdownAdaptiveThresholdEnabled = true,
                AthDrawdownAdaptiveBasePercent = 100.0,
                AthDrawdownAdaptiveStepPercent = 7.0,
                AthDrawdownAdaptiveCycleStartYears = [2011, 2013, 2017, 2021, 2026],
                AthDrawdownAdaptiveMinThresholdPercent = 1.0,
                AthDrawdownAdaptiveMaxThresholdPercent = 99.0
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        bearMarketFilterService.Verify(
            b => b.IsDeepCycleLowAsync("1h", utcDate.ToUtcDateTime(), 65.0, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task HandleSell_InDeepCycleLow_SuppressesRegularSell()
    {
        // Position open, regular Sell signal fires while price is ≥75% below ATH → must be ignored.
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = utcDate.AddDays(-30), TradeAdvice = "FireSell", Price = 110m };
        var latestBuy = new BuyActionLog { TradingDate = utcDate.AddDays(-10), TradeAdvice = "Buy", Price = 100m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Sell", Price = 20m }; // deep in the hole

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, latestBuy));
        _mockTradingRepository.Setup(r => r.FindLatestFireSellAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync(latestSell);
        _mockTradingRepository.Setup(r => r.FindLatestPanicBuyAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync(latestBuy);

        var sut = BuildServiceWithBearFilter(suppressEnabled: true, isDeepCycleLow: true);
        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.IsAny<SellActionLog>(), CancellationToken.None), Times.Never);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task HandleSell_SuppressDisabled_RegularSellExecutes()
    {
        // Suppression is off → regular Sell proceeds normally even if price is at deep-cycle-low.
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = utcDate.AddDays(-30), TradeAdvice = "FireSell", Price = 110m };
        var latestBuy = new BuyActionLog { TradingDate = utcDate.AddDays(-10), TradeAdvice = "Buy", Price = 100m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Sell", Price = 20m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, latestBuy));
        _mockTradingRepository.Setup(r => r.SaveSellActionLog(It.IsAny<SellActionLog>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var sut = BuildServiceWithBearFilter(suppressEnabled: false, isDeepCycleLow: true);
        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.IsAny<SellActionLog>(), CancellationToken.None), Times.Once);
    }

    [Test]
    public async Task TradeOpportunity_OpenPositionInCycleTopZone_UpgradesWaitToDistributionSell()
    {
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = utcDate.AddDays(-50), TradeAdvice = "Sell", Price = 50_000m };
        var latestBuy = new BuyActionLog { TradingDate = utcDate.AddDays(-10), TradeAdvice = "Buy", Price = 80_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Wait", Price = 84_000m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, latestBuy));
        _mockTradingRepository.Setup(r => r.SaveSellActionLog(
                It.Is<SellActionLog>(s => s.TradeAdvice == "DistributionSell"),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsCycleTopOvervaluedAsync("1d", utcDate.ToUtcDateTime(), 1460, 350.0, 90.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                CycleTopSellZoneEnabled = true,
                CycleTopSellZoneInterval = "1d",
                CycleTopSellZoneLookbackDays = 1460,
                CycleTopSellZoneMinDrawupPercent = 350.0,
                CycleTopSellZoneMinAthProximityPercent = 90.0
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(
            r => r.SaveSellActionLog(It.Is<SellActionLog>(s => s.TradeAdvice == "DistributionSell"), CancellationToken.None),
            Times.Once);
    }

    // ── ADR-008: Buy-Blocking Gate (tight-loop guard) ──────────────────────

    [Test]
    public async Task TradeOpportunity_BuySignalWhenCycleTopZoneActive_BlocksBuy()
    {
        // When CycleTopSellZone is enabled and active, a buy signal must not execute.
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = utcDate.AddHours(-2), TradeAdvice = "DistributionSell", Price = 84_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 84_000m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, (BuyActionLog?)null));

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsCycleTopOvervaluedAsync("1d", utcDate.ToUtcDateTime(), 1460, 350.0, 90.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // AthBreakoutBuy path: no open position + latest DistributionSell → GetCycleAthAsync is called
        bearMarketFilterService
            .Setup(b => b.GetCycleAthAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.0);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                CycleTopSellZoneEnabled = true,
                CycleTopSellZoneInterval = "1d",
                CycleTopSellZoneLookbackDays = 1460,
                CycleTopSellZoneMinDrawupPercent = 350.0,
                CycleTopSellZoneMinAthProximityPercent = 90.0
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), CancellationToken.None), Times.Never);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task TradeOpportunity_BuySignalWhenAthProximityTooHigh_BlocksBuy()
    {
        // When AthProximityBuyBlock is enabled and price is above 85% of ATH, buy must NOT execute.
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = utcDate.AddHours(-2), TradeAdvice = "DistributionSell", Price = 84_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 90_000m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, (BuyActionLog?)null));

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        bearMarketFilterService
            .Setup(b => b.IsAthProximityTooHighForBuyAsync("1d", utcDate.ToUtcDateTime(), 85.0, 180, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                AthDrawdownOverrideEnabled = true,
                AthProximityBuyBlockEnabled = true,
                AthProximityBuyBlockMaxPercent = 85.0,
                AthProximityBuyBlockInterval = "1d",
                AthProximityBuyBlockMaxAthAgeDays = 180
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), CancellationToken.None), Times.Never);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task TradeOpportunity_BuySignalWhenAthProximityNotTooHigh_AllowsBuy()
    {
        // When AthProximityBuyBlock is enabled but price is below 85% of ATH, buy must execute.
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = utcDate.AddHours(-2), TradeAdvice = "DistributionSell", Price = 40_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 42_000m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, (BuyActionLog?)null));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        bearMarketFilterService
            .Setup(b => b.IsAthProximityTooHighForBuyAsync("1d", utcDate.ToUtcDateTime(), 85.0, 180, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                AthDrawdownOverrideEnabled = true,
                AthProximityBuyBlockEnabled = true,
                AthProximityBuyBlockMaxPercent = 85.0,
                AthProximityBuyBlockInterval = "1d",
                AthProximityBuyBlockMaxAthAgeDays = 180
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), CancellationToken.None), Times.Once);
    }

    // ── NearAthSellSuppress gate ──────────────────────────────────────────

    [Test]
    public async Task HandleSell_NearAthSellSuppressEnabled_RegularSellSuppressedWhenNearAth()
    {
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = utcDate.AddDays(-30), TradeAdvice = "Sell", Price = 50_000m };
        var latestBuy = new BuyActionLog { TradingDate = utcDate.AddDays(-10), TradeAdvice = "Buy", Price = 60_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Sell", Price = 70_000m }; // near ATH

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, latestBuy));

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsNearAthForSellSuppressAsync("1d", utcDate.ToUtcDateTime(), 70.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                NearAthSellSuppressEnabled = true,
                NearAthSellSuppressMinPercent = 70.0,
                NearAthSellSuppressInterval = "1d"
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.IsAny<SellActionLog>(), CancellationToken.None), Times.Never);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task HandleSell_NearAthSellSuppressEnabled_NotNearAth_AllowsSell()
    {
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = utcDate.AddDays(-30), TradeAdvice = "Sell", Price = 50_000m };
        var latestBuy = new BuyActionLog { TradingDate = utcDate.AddDays(-10), TradeAdvice = "Buy", Price = 60_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Sell", Price = 40_000m }; // far from ATH

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, latestBuy));
        _mockTradingRepository.Setup(r => r.SaveSellActionLog(It.IsAny<SellActionLog>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsCycleTopOvervaluedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        bearMarketFilterService
            .Setup(b => b.IsNearAthForSellSuppressAsync("1d", utcDate.ToUtcDateTime(), 70.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                CycleTopSellZoneEnabled = true,
                NearAthSellSuppressEnabled = true,
                NearAthSellSuppressMinPercent = 70.0,
                NearAthSellSuppressInterval = "1d"
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveSellActionLog(It.IsAny<SellActionLog>(), CancellationToken.None), Times.Once);
    }

    // ── PostCycleTopBuyBlock gate ─────────────────────────────────────────

    [Test]
    public async Task TradeOpportunity_BuyWithin90DaysAfterDistributionSell_BlocksBuy()
    {
        // The last sell was a DistributionSell 30 days ago — buy must be suppressed for 90d.
        var sellDate = DateTime.UtcNow.AddDays(-30);
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = sellDate, TradeAdvice = "DistributionSell", Price = 60_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 45_000m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, (BuyActionLog?)null));

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                AthDrawdownOverrideEnabled = true,
                PostCycleTopBuyBlockEnabled = true,
                PostCycleTopBuyBlockDays = 90
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), CancellationToken.None), Times.Never);
        _mockTelegramBotService.Verify(s => s.SendMessageAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task TradeOpportunity_BuyAfter90DaysPostDistributionSell_AllowsBuy()
    {
        // The last DistributionSell was 95 days ago — block has expired, buy must execute.
        var sellDate = DateTime.UtcNow.AddDays(-95);
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = sellDate, TradeAdvice = "DistributionSell", Price = 60_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 30_000m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, (BuyActionLog?)null));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                AthDrawdownOverrideEnabled = true,
                PostCycleTopBuyBlockEnabled = true,
                PostCycleTopBuyBlockDays = 90
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), CancellationToken.None), Times.Once);
    }

    // ── PostCycleTopSmaRatioBlock gate ────────────────────────────────────

    [Test]
    public async Task TradeOpportunity_PriceAboveSmaRatioAfterDistributionSell_BlocksBuy()
    {
        // 90-day time-block has expired (95 days ago), but price/SMA(730d) = 1.6 > 1.5 → blocked.
        var sellDate = DateTime.UtcNow.AddDays(-95);
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = sellDate, TradeAdvice = "DistributionSell", Price = 60_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 43_000m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, (BuyActionLog?)null));

        // 729 candles close=10000, last close=16000 → ratio=1.6 > 1.5 (blocked)
        var smaCandles = BuildSmaCandles(730, uniformClose: 10_000.0, lastClose: 16_000.0, baseTime: utcDate);
        _mockBinanceKlineService
            .Setup(b => b.GetCandlesRangeAsync("1d", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(smaCandles);

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                AthDrawdownOverrideEnabled = true,
                PostCycleTopBuyBlockEnabled = true,
                PostCycleTopBuyBlockDays = 90,
                PostCycleTopSmaRatioBlockEnabled = true,
                PostCycleTopSmaRatioPeriodDays = 730,
                PostCycleTopSmaRatioMaxMultiplier = 1.5
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), CancellationToken.None), Times.Never);
    }

    [Test]
    public async Task TradeOpportunity_PriceBelowSmaRatioAfterDistributionSell_AllowsBuy()
    {
        // 90-day time-block has expired (95 days ago) and price/SMA(730d) = 1.2 < 1.5 → allowed.
        var sellDate = DateTime.UtcNow.AddDays(-95);
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = sellDate, TradeAdvice = "DistributionSell", Price = 60_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 28_000m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, (BuyActionLog?)null));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        // 729 candles close=10000, last close=12000 → ratio=1.2 < 1.5 (allowed)
        var smaCandles = BuildSmaCandles(730, uniformClose: 10_000.0, lastClose: 12_000.0, baseTime: utcDate);
        _mockBinanceKlineService
            .Setup(b => b.GetCandlesRangeAsync("1d", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(smaCandles);

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                AthDrawdownOverrideEnabled = true,
                PostCycleTopBuyBlockEnabled = true,
                PostCycleTopBuyBlockDays = 90,
                PostCycleTopSmaRatioBlockEnabled = true,
                PostCycleTopSmaRatioPeriodDays = 730,
                PostCycleTopSmaRatioMaxMultiplier = 1.5
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), CancellationToken.None), Times.Once);
    }

    // ── PostCycleTopAthDrawdownBlock gate ─────────────────────────────────

    [Test]
    public async Task TradeOpportunity_PriceAboveAthDrawdownThresholdAfterDistributionSell_BlocksBuy()
    {
        // Jan 2022 analogy: DistributionSell 95 days ago (time-block expired),
        // price = 43_000 / ATH 69_000 = 62.3 % → above 50 % threshold → blocked.
        var sellDate = DateTime.UtcNow.AddDays(-95);
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = sellDate, TradeAdvice = "DistributionSell", Price = 60_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 43_000m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, (BuyActionLog?)null));

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // ATH-drawdown gate returns true → price too high relative to ATH → block
        bearMarketFilterService
            .Setup(b => b.IsAthProximityTooHighForBuyAsync("1d", utcDate, 50.0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                AthDrawdownOverrideEnabled = true,
                PostCycleTopBuyBlockEnabled = true,
                PostCycleTopBuyBlockDays = 90,
                PostCycleTopAthDrawdownBlockEnabled = true,
                PostCycleTopAthDrawdownMaxAthProximityPercent = 50.0,
                AthProximityBuyBlockInterval = "1d"
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), CancellationToken.None), Times.Never);
    }

    [Test]
    public async Task TradeOpportunity_PriceBelowAthDrawdownThresholdAfterDistributionSell_AllowsBuy()
    {
        // Mar 2018 analogy: DistributionSell 95 days ago (time-block expired),
        // price = 7_900 / ATH 20_000 = 39.5 % → below 50 % threshold → allowed.
        var sellDate = DateTime.UtcNow.AddDays(-95);
        var utcDate = DateTime.UtcNow;
        var latestSell = new SellActionLog { TradingDate = sellDate, TradeAdvice = "DistributionSell", Price = 20_000m };
        var deal = new Deal { TradingDate = utcDate, TradeAdvice = "Buy", Price = 7_900m };

        _mockTradingRepository.Setup(r => r.FindLatestAction(utcDate.ToUtcDateTime()))
            .ReturnsAsync((latestSell, (BuyActionLog?)null));
        _mockTradingRepository.Setup(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockTelegramBotService.Setup(s => s.SendMessageAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockActionLogService.Setup(s => s.SaveAsync(It.IsAny<ActionLog>()))
            .Returns(Task.CompletedTask);

        var bearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        bearMarketFilterService
            .Setup(b => b.IsDeepCycleLowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // ATH-drawdown gate returns false → price sufficiently below ATH → allow
        bearMarketFilterService
            .Setup(b => b.IsAthProximityTooHighForBuyAsync("1d", utcDate, 50.0, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = new TradeOpportunityService(
            _mockTradingRepository.Object,
            _mockTelegramBotService.Object,
            _mockActionLogService.Object,
            _mockBinanceKlineService.Object,
            bearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                MacroOnlyMode = true,
                AthDrawdownOverrideEnabled = true,
                PostCycleTopBuyBlockEnabled = true,
                PostCycleTopBuyBlockDays = 90,
                PostCycleTopAthDrawdownBlockEnabled = true,
                PostCycleTopAthDrawdownMaxAthProximityPercent = 50.0,
                AthProximityBuyBlockInterval = "1d"
            }),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

        await sut.TradeOpportunity(deal, CancellationToken.None);

        _mockTradingRepository.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), CancellationToken.None), Times.Once);
    }

    private static IReadOnlyList<OhlcvCandle> BuildSmaCandles(int count, double uniformClose, double lastClose, DateTime baseTime)
    {
        var candles = new List<OhlcvCandle>(count);
        var start = baseTime.AddDays(-count);
        for (var i = 0; i < count - 1; i++)
        {
            var t = start.AddDays(i);
            candles.Add(new OhlcvCandle(t, uniformClose, uniformClose, uniformClose, uniformClose, 0, t.AddDays(1)));
        }
        var last = start.AddDays(count - 1);
        candles.Add(new OhlcvCandle(last, lastClose, lastClose, lastClose, lastClose, 0, last.AddDays(1)));
        return candles;
    }
}

