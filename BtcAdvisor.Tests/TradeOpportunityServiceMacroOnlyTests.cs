using Moq;
using Microsoft.Extensions.Options;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

/// <summary>
/// Tests for MacroOnlyMode in TradeOpportunityService (ADR-017).
/// Verifies that:
///  - Incoming micro-signal advice is ignored
///  - CycleTopZone → DistributionSell when position is open
///  - DeepCycleLow → StrongBuy when no position
///  - ATH-breakout re-entry fires when price > cycleAth × 1.01 after DistributionSell
///  - ATH-breakout chaining is suppressed when last entry was itself AthBreakoutBuy
/// </summary>
[TestFixture]
public class TradeOpportunityServiceMacroOnlyTests
{
    private Mock<ITradingRepository> _repo;
    private Mock<ITelegramBotService> _telegram;
    private Mock<IActionLogService> _actionLog;
    private Mock<IBinanceKlineService> _kline;
    private Mock<IBearMarketFilterService> _bearFilter;

    private static readonly DateTime Now = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void SetUp()
    {
        _repo          = new Mock<ITradingRepository>(MockBehavior.Strict);
        _telegram      = new Mock<ITelegramBotService>(MockBehavior.Strict);
        _actionLog     = new Mock<IActionLogService>(MockBehavior.Strict);
        _kline         = new Mock<IBinanceKlineService>(MockBehavior.Strict);
        _bearFilter    = new Mock<IBearMarketFilterService>(MockBehavior.Strict);

        // Telegram / log pass-through for buy/sell messages
        _telegram.Setup(t => t.SendMessageAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        _actionLog.Setup(a => a.SaveAsync(It.IsAny<ActionLog>())).Returns(Task.CompletedTask);
    }

    private TradeOpportunityService BuildService(BearMarketFilterOptions opts)
        => new TradeOpportunityService(
            _repo.Object,
            _telegram.Object,
            _actionLog.Object,
            _kline.Object,
            _bearFilter.Object,
            Options.Create(opts),
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 0 }));

    private static BearMarketFilterOptions MacroOnlyOptions() => new BearMarketFilterOptions
    {
        MacroOnlyMode = true,
        CycleTopSellZoneEnabled = true,
        CycleTopSellZoneInterval = "1d",
        CycleTopSellZoneLookbackDays = 1460,
        CycleTopSellZoneMinDrawupPercent = 350.0,
        CycleTopSellZoneMinAthProximityPercent = 98.0,
        AthDrawdownOverrideEnabled = true,
        AthDrawdownInterval = "1h",
        AthDrawdownOverrideThresholdPercent = 75.0,
    };

    // -------------------------------------------------------------------------
    // 1. Incoming micro-signal advice is IGNORED in macroOnly mode
    // -------------------------------------------------------------------------
    [Test]
    public async Task MacroOnly_IncomingBuyAdvice_IgnoredWhenNoMacroSignal()
    {
        // No position, no deep-cycle-low, no cycle ATH breakout → incoming "StrongBuy" must be ignored.
        var opts = MacroOnlyOptions();
        var svc = BuildService(opts);

        _repo.Setup(r => r.FindLatestAction(It.IsAny<DateTime>()))
             .ReturnsAsync(((SellActionLog?)null, (BuyActionLog?)null));

        _bearFilter.Setup(b => b.IsDeepCycleLowAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // No previous DistributionSell → AthBreakout path is not reached (latestSell == null).

        var deal = new Deal { TradingDate = Now, TradeAdvice = "StrongBuy", Price = 50_000m };
        await svc.TradeOpportunity(deal, CancellationToken.None);

        _repo.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // 2. CycleTopZone → DistributionSell when position is open
    // -------------------------------------------------------------------------
    [Test]
    public async Task MacroOnly_CycleTopZoneActive_ForcesDistributionSell()
    {
        var opts = MacroOnlyOptions();
        var svc = BuildService(opts);

        var buy = new BuyActionLog { TradingDate = Now.AddDays(-10), Price = 40_000m, TradeAdvice = "StrongBuy" };
        _repo.Setup(r => r.FindLatestAction(It.IsAny<DateTime>()))
             .ReturnsAsync(((SellActionLog?)null, buy));

        _bearFilter.Setup(b => b.IsCycleTopOvervaluedAsync(
                "1d", It.IsAny<DateTime>(), 1460, 350.0, 98.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _repo.Setup(r => r.SaveSellActionLog(
                It.Is<SellActionLog>(s => s.TradeAdvice == "DistributionSell"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var deal = new Deal { TradingDate = Now, TradeAdvice = "Wait", Price = 69_000m };
        await svc.TradeOpportunity(deal, CancellationToken.None);

        _repo.Verify(r => r.SaveSellActionLog(
            It.Is<SellActionLog>(s => s.TradeAdvice == "DistributionSell"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // 3. DeepCycleLow → StrongBuy when no position
    // -------------------------------------------------------------------------
    [Test]
    public async Task MacroOnly_DeepCycleLow_TriggersStrongBuy()
    {
        var opts = MacroOnlyOptions();
        var svc = BuildService(opts);

        _repo.Setup(r => r.FindLatestAction(It.IsAny<DateTime>()))
             .ReturnsAsync(((SellActionLog?)null, (BuyActionLog?)null));

        _bearFilter.Setup(b => b.IsDeepCycleLowAsync(
                "1h", It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // HandleBuy still checks CycleTopSellZoneEnabled gate (entry guard) even in macroOnly path.
        // At a deep cycle low (~$15k) the zone is clearly inactive.
        _bearFilter.Setup(b => b.IsCycleTopOvervaluedAsync(
                "1d", It.IsAny<DateTime>(), 1460, 350.0, 98.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repo.Setup(r => r.SaveBuyActionLog(
                It.Is<BuyActionLog>(b => b.TradeAdvice == "StrongBuy"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var deal = new Deal { TradingDate = Now, TradeAdvice = "Wait", Price = 15_000m };
        await svc.TradeOpportunity(deal, CancellationToken.None);

        _repo.Verify(r => r.SaveBuyActionLog(
            It.Is<BuyActionLog>(b => b.TradeAdvice == "StrongBuy"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // 4. ATH-breakout re-entry after DistributionSell when price > cycleAth × 1.01
    // -------------------------------------------------------------------------
    [Test]
    public async Task MacroOnly_AthBreakout_BuysAboveCycleAth()
    {
        var opts = MacroOnlyOptions();
        var svc = BuildService(opts);

        var sellDate = Now.AddDays(-30);
        var priorBuy = new BuyActionLog { TradingDate = sellDate.AddDays(-180), Price = 20_000m, TradeAdvice = "StrongBuy" };
        var latestSell = new SellActionLog
        {
            TradingDate = sellDate,
            TradeAdvice = "DistributionSell",
            Price = 69_000m,
            BuyActionLog = priorBuy
        };
        _repo.Setup(r => r.FindLatestAction(It.IsAny<DateTime>()))
             .ReturnsAsync((latestSell, (BuyActionLog?)null));

        _bearFilter.Setup(b => b.IsDeepCycleLowAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        const double cycleAth = 69_000.0;
        _bearFilter.Setup(b => b.GetCycleAthAsync(
                "1d", It.IsAny<DateTime>(), 1460, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cycleAth);

        // Price above cycleAth * 1.01 = 69_690
        var deal = new Deal { TradingDate = Now, TradeAdvice = "Wait", Price = 70_000m };

        // HandleBuy gates: no post-cycle-top block configured
        _repo.Setup(r => r.SaveBuyActionLog(
                It.Is<BuyActionLog>(b => b.TradeAdvice == "AthBreakoutBuy"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await svc.TradeOpportunity(deal, CancellationToken.None);

        _repo.Verify(r => r.SaveBuyActionLog(
            It.Is<BuyActionLog>(b => b.TradeAdvice == "AthBreakoutBuy"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // 5. ATH-breakout chaining suppressed when last entry was itself AthBreakoutBuy
    // -------------------------------------------------------------------------
    [Test]
    public async Task MacroOnly_AthBreakout_Suppressed_WhenLastEntryWasAthBreakout()
    {
        var opts = MacroOnlyOptions();
        var svc = BuildService(opts);

        var sellDate = Now.AddDays(-10);
        var priorBuy = new BuyActionLog
        {
            TradingDate = sellDate.AddDays(-5),
            Price = 69_500m,
            TradeAdvice = "AthBreakoutBuy"  // last entry was AthBreakoutBuy → suppress chaining
        };
        var latestSell = new SellActionLog
        {
            TradingDate = sellDate,
            TradeAdvice = "DistributionSell",
            Price = 72_000m,
            BuyActionLog = priorBuy
        };
        _repo.Setup(r => r.FindLatestAction(It.IsAny<DateTime>()))
             .ReturnsAsync((latestSell, (BuyActionLog?)null));

        _bearFilter.Setup(b => b.IsDeepCycleLowAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // GetCycleAthAsync should NOT be called (chaining suppressed before that check)
        _bearFilter.Setup(b => b.GetCycleAthAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(69_000.0);

        var deal = new Deal { TradingDate = Now, TradeAdvice = "Wait", Price = 75_000m };
        await svc.TradeOpportunity(deal, CancellationToken.None);

        _repo.Verify(r => r.SaveBuyActionLog(It.IsAny<BuyActionLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // 6. AthBreakoutBuy bypasses PostCycleTopBuyBlockDays gate
    // -------------------------------------------------------------------------
    [Test]
    public async Task MacroOnly_AthBreakout_BypassesPostCycleTopDayBlock()
    {
        var opts = MacroOnlyOptions();
        opts.PostCycleTopBuyBlockEnabled = true;
        opts.PostCycleTopBuyBlockDays = 9999; // normally blocks forever
        var svc = BuildService(opts);

        var sellDate = Now.AddDays(-5); // within 9999 day block
        var priorBuy = new BuyActionLog { TradingDate = sellDate.AddDays(-100), Price = 20_000m, TradeAdvice = "StrongBuy" };
        var latestSell = new SellActionLog
        {
            TradingDate = sellDate,
            TradeAdvice = "DistributionSell",
            Price = 69_000m,
            BuyActionLog = priorBuy
        };
        _repo.Setup(r => r.FindLatestAction(It.IsAny<DateTime>()))
             .ReturnsAsync((latestSell, (BuyActionLog?)null));

        _bearFilter.Setup(b => b.IsDeepCycleLowAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _bearFilter.Setup(b => b.GetCycleAthAsync(
                "1d", It.IsAny<DateTime>(), 1460, It.IsAny<CancellationToken>()))
            .ReturnsAsync(69_000.0);

        // Price above cycleAth * 1.01 → AthBreakoutBuy path
        var deal = new Deal { TradingDate = Now, TradeAdvice = "Wait", Price = 70_000m };

        // AthBreakoutBuy should still bypass the PostCycleTopBuyBlockDays gate
        _repo.Setup(r => r.SaveBuyActionLog(
                It.Is<BuyActionLog>(b => b.TradeAdvice == "AthBreakoutBuy"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await svc.TradeOpportunity(deal, CancellationToken.None);

        _repo.Verify(r => r.SaveBuyActionLog(
            It.Is<BuyActionLog>(b => b.TradeAdvice == "AthBreakoutBuy"), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
