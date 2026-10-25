using Moq;
using Microsoft.Extensions.Options;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class BacktestServiceTests
{
    private Mock<IBitcoinTradeAdvisor> _mockTradeAdvisor;
    private Mock<IStockCandleRepository> _mockStockCandleRepository;
    private Mock<IBinanceKlineService> _mockBinanceKlineService;
    private Mock<IMarketRegimeService> _mockMarketRegimeService;
    private Mock<IBacktestRiskControlService> _mockRiskControlService;
    private Mock<ITradingRepository> _mockTradingRepository;
    private Mock<IAtrLookupService> _mockAtrLookupService;
    private Mock<IMarketContextService> _mockMarketContextService;
    private Mock<IBearMarketFilterService> _mockBearMarketFilterService;
    private BacktestService _sut;

    private static readonly DateTime T0 = new(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void Setup()
    {
        _mockTradeAdvisor = new Mock<IBitcoinTradeAdvisor>(MockBehavior.Strict);
        _mockStockCandleRepository = new Mock<IStockCandleRepository>(MockBehavior.Strict);
        _mockBinanceKlineService = new Mock<IBinanceKlineService>(MockBehavior.Strict);
        _mockMarketRegimeService = new Mock<IMarketRegimeService>(MockBehavior.Strict);
        _mockRiskControlService = new Mock<IBacktestRiskControlService>(MockBehavior.Strict);
        _mockTradingRepository = new Mock<ITradingRepository>(MockBehavior.Strict);
        _mockAtrLookupService = new Mock<IAtrLookupService>(MockBehavior.Strict);

        _mockMarketRegimeService
            .Setup(s => s.GetRegimeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketRegimeSnapshot(MarketRegime.Neutral, 0d, null, null, null));

        _mockRiskControlService
            .Setup(r => r.ShouldTriggerHardStopLoss(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<double>()))
            .Returns(false);

        _mockRiskControlService
            .Setup(r => r.ShouldTriggerAtrStop(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<double?>(), It.IsAny<double>()))
            .Returns(false);

        _mockRiskControlService
            .Setup(r => r.EvaluateHardStop(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal?>(), It.IsAny<double>(), It.IsAny<double>()))
            .Returns(new StopLossEvaluation(false, 0m));

        _mockRiskControlService
            .Setup(r => r.EvaluateAtrStop(It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal?>(), It.IsAny<double?>(), It.IsAny<double>(), It.IsAny<double>()))
            .Returns(new StopLossEvaluation(false, 0m));

        _mockAtrLookupService
            .Setup(a => a.GetAtrAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((double?)null);

        _mockMarketContextService = new Mock<IMarketContextService>(MockBehavior.Strict);
        _mockMarketContextService
            .Setup(m => m.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketContextSnapshot(null, null, null));

        _mockBearMarketFilterService = new Mock<IBearMarketFilterService>(MockBehavior.Strict);
        _mockBearMarketFilterService
            .Setup(b => b.IsBearMarketAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _sut = new BacktestService(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions()),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                Enabled = false,
                // Disable post-halving window gate in default fixture so synthetic
                // test timestamps (T0 = 2025-01-01) are not blocked from the
                // CycleTopZone. Halving-gate tests opt in explicitly.
                CycleTopSellZoneMinMonthsAfterHalving = 0,
                CycleTopSellZoneMaxMonthsAfterHalving = 0,
            }));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static BitcoinTradeStatus MakeStatus(
        string advice,
        double price,
        DecisionSource decisionSource = DecisionSource.Unknown,
        double linearBullishScore = 0d) =>
        new() { TradeAdvice = advice, CurrentPrice = price, DecisionSource = decisionSource, LinearBullishScore = linearBullishScore };

    private void SetupTimestamps(params DateTime[] timestamps) =>
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("2h", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)timestamps
                .Select(t => new OhlcvCandle(
                    OpenTime: t.AddHours(-2),
                    Open: 0,
                    High: 0,
                    Low: 0,
                    Close: 0,
                    Volume: 0,
                    CloseTime: t))
                .ToList()
                .AsReadOnly());

    private void SetupMicroStrategyUnavailable() =>
        _mockStockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StockCandle>)new List<StockCandle>().AsReadOnly());

    private void SetupAdvice(
        DateTime timestamp,
        string advice,
        double price,
        DecisionSource decisionSource = DecisionSource.Unknown,
        double linearBullishScore = 0d) =>
        _mockTradeAdvisor
            .Setup(a => a.GetBitcoinTradeAdvice(
                timestamp, timestamp,
                It.IsAny<decimal?>(), It.IsAny<decimal?>(),
                It.IsAny<decimal?>(), It.IsAny<decimal?>(),
                It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)new[] { MakeStatus(advice, price, decisionSource, linearBullishScore) });

    // ── Tests: Leerfall ───────────────────────────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_WhenNoTimestamps_ReturnsEmptySummary()
    {
        // Arrange
        SetupTimestamps();

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.TimestampsEvaluated, Is.EqualTo(0));
            Assert.That(result.TotalBuys, Is.EqualTo(0));
            Assert.That(result.TotalSells, Is.EqualTo(0));
            Assert.That(result.Trades, Is.Empty);
        }
    }

    // ── Tests: Wait-Signale ───────────────────────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_WhenOnlyWaitSignals_NoTradesAreRecorded()
    {
        // Arrange
        SetupTimestamps(T0, T0.AddMinutes(1));
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "Wait", 50_000.0);
        SetupAdvice(T0.AddMinutes(1), "Wait", 51_000.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.TotalBuys, Is.Zero);
            Assert.That(result.TotalSells, Is.Zero);
            Assert.That(result.Trades, Is.Empty);
        }
    }

    // ── Tests: Einzelner Kauf ─────────────────────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_SingleBuySignal_RecordsBuyTrade()
    {
        // Arrange
        SetupTimestamps(T0);
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "StrongBuy", 40_000.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.TotalBuys, Is.EqualTo(1));
            Assert.That(result.TotalSells, Is.EqualTo(0));
            Assert.That(result.Trades, Has.Count.EqualTo(1));
            Assert.That(result.Trades[0].TradeAdvice, Is.EqualTo("StrongBuy"));
            Assert.That(result.Trades[0].Price, Is.EqualTo(40_000m));
            Assert.That(result.Trades[0].ProfitPercent, Is.Null);
        }
    }

    [Test]
    public async Task RunBacktestAsync_WithCustomConfluenceThresholds_UsesAdvisorOverloadWithThresholdSet()
    {
        var thresholds = new LongTimeframeConfluenceThresholdSet(
            bullishMinAlignedRanges: 2,
            bullishMinTrendCountSum: 2,
            bearishMinAlignedRanges: 4,
            bearishMinTrendCountSum: 3);

        SetupTimestamps(T0);
        SetupMicroStrategyUnavailable();
        _mockTradeAdvisor
            .Setup(a => a.GetBitcoinTradeAdvice(
                T0,
                T0,
                It.IsAny<decimal?>(),
                It.IsAny<decimal?>(),
                It.IsAny<decimal?>(),
                It.IsAny<decimal?>(),
                It.IsAny<BuyActionLog?>(),
                It.IsAny<BuyActionLog?>(),
                It.IsAny<CancellationToken>(),
                thresholds))
            .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)new[] { MakeStatus("StrongBuy", 40_000.0) });

        var result = await _sut.RunBacktestAsync(
            T0,
            T0.AddHours(1),
            CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: thresholds);

        Assert.That(result.TotalBuys, Is.EqualTo(1));
        _mockTradeAdvisor.VerifyAll();
    }

    // ── Tests: Kauf + Verkauf ──────────────────────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_BuyThenFireSell_CalculatesProfitCorrectly()
    {
        // Arrange – Kauf bei 40k, FireSell bei 44k → +10%
        var t1 = T0;
        var t2 = T0.AddMinutes(1);
        SetupTimestamps(t1, t2);
        SetupMicroStrategyUnavailable();
        SetupAdvice(t1, "StrongBuy", 40_000.0);
        SetupAdvice(t2, "FireSellStrong", 44_000.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        var sell = result.Trades.Single(t => t.TradeAdvice.Contains("Sell"));

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.TotalBuys, Is.EqualTo(1));
            Assert.That(result.TotalSells, Is.EqualTo(1));
            Assert.That(sell.ProfitPercent, Is.EqualTo(10.0).Within(0.001));
            Assert.That(result.TotalProfitPercent, Is.EqualTo(10.0).Within(0.01));
            Assert.That(result.WinRatePercent, Is.EqualTo(100.0));
        }
    }

    [Test]
    public async Task RunBacktestAsync_BuyThenFireSell_AtLoss_CalculatesNegativeProfit()
    {
        // Arrange – Kauf bei 50k, FireSell bei 47.5k → -5%
        var t1 = T0;
        var t2 = T0.AddMinutes(1);
        SetupTimestamps(t1, t2);
        SetupMicroStrategyUnavailable();
        SetupAdvice(t1, "StrictBuy", 50_000.0);
        SetupAdvice(t2, "FireSellStrict", 47_500.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        var sell = result.Trades.Single(t => t.TradeAdvice.Contains("Sell"));

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(sell.ProfitPercent, Is.EqualTo(-5.0).Within(0.001));
            Assert.That(result.WinRatePercent, Is.EqualTo(0.0));
            Assert.That(result.MaxLossPercent, Is.EqualTo(-5.0).Within(0.001));
        }
    }

    [Test]
    public async Task RunBacktestAsync_SellWithoutPriorBuy_IsSkipped()
    {
        // Arrange – kein vorheriger Kauf in der Simulation
        SetupTimestamps(T0);
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "FireSellStrong", 45_000.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.Trades, Is.Empty);
            Assert.That(result.TotalSells, Is.Zero);
            Assert.That(result.TotalProfitPercent, Is.Null);
            Assert.That(result.WinRatePercent, Is.Null);
        }
    }

    [Test]
    public async Task RunBacktestAsync_WhenBullRegime_BlocksBearishConfluenceSell()
    {
        var t1 = T0;
        var t2 = T0.AddMinutes(1);
        SetupTimestamps(t1, t2);
        SetupMicroStrategyUnavailable();
        SetupAdvice(t1, "Buy", 40_000.0, DecisionSource.LongTimeframeMultiTrendBullish);
        SetupAdvice(t2, "Sell", 44_000.0, DecisionSource.LongTimeframeMultiTrendBearish);
        _mockMarketRegimeService
            .Setup(s => s.GetRegimeAsync(t2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketRegimeSnapshot(MarketRegime.Bull, 44_000d, 43_000d, 39_000d, 25d));

        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        Assert.That(result.TotalSells, Is.Zero);
    }

    [Test]
    public async Task RunBacktestAsync_WhenBearRegime_AllowsStrongLinearBullishBuy()
    {
        SetupTimestamps(T0);
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "Buy", 40_000.0, DecisionSource.LongTimeframeLinearBullish, linearBullishScore: 24d);
        _mockMarketRegimeService
            .Setup(s => s.GetRegimeAsync(T0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketRegimeSnapshot(MarketRegime.Bear, 40_000d, 39_000d, 42_000d, 28d));

        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        Assert.That(result.TotalBuys, Is.EqualTo(1));
    }

    [Test]
    public async Task RunBacktestAsync_WhenBearRegime_RespectsCustomLinearBullishScoreThreshold()
    {
        SetupTimestamps(T0);
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "Buy", 40_000.0, DecisionSource.LongTimeframeLinearBullish, linearBullishScore: 24d);
        _mockMarketRegimeService
            .Setup(s => s.GetRegimeAsync(T0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketRegimeSnapshot(MarketRegime.Bear, 40_000d, 39_000d, 42_000d, 28d));

        var result = await _sut.RunBacktestAsync(
            T0,
            T0.AddHours(1),
            CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: 25d);

        Assert.That(result.TotalBuys, Is.Zero);
    }

    // ── Tests: Mehrere Zyklen ─────────────────────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_TwoCycles_AggregatesProfitCorrectly()
    {
        // Zyklus 1: 40k→50k (+25%), Zyklus 2: 45k→49.5k (+10%) => gesamt +35%
        var ts = new[]
        {
            T0,
            T0.AddMinutes(1),
            T0.AddMinutes(2),
            T0.AddMinutes(3),
        };
        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        SetupAdvice(ts[0], "StrongBuy", 40_000.0);
        SetupAdvice(ts[1], "FireSellStrong", 50_000.0);
        SetupAdvice(ts[2], "StrongBuy", 45_000.0);
        SetupAdvice(ts[3], "FireSellStrong", 49_500.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.TotalBuys, Is.EqualTo(2));
            Assert.That(result.TotalSells, Is.EqualTo(2));
            Assert.That(result.TotalProfitPercent, Is.EqualTo(35.0).Within(0.1));
            Assert.That(result.WinRatePercent, Is.EqualTo(100.0));
            Assert.That(result.MaxGainPercent, Is.EqualTo(25.0).Within(0.1));
        }
    }

    [Test]
    public async Task RunBacktestAsync_BuyBelowPreviousAcceptedBuy_IsAcceptedAfterFireSell()
    {
        // Cross-cycle buy: after the position is closed by a FireSell, the buy anchor must reset
        // so a new entry at a lower price than the previous accepted buy is allowed.
        var ts = new[]
        {
            T0,
            T0.AddMinutes(1),
            T0.AddMinutes(2),
        };

        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        SetupAdvice(ts[0], "StrongBuy", 40_000.0);
        SetupAdvice(ts[1], "FireSellStrong", 44_000.0);
        SetupAdvice(ts[2], "StrongBuy", 39_000.0);

        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys, Is.EqualTo(2));
            Assert.That(result.TotalSells, Is.EqualTo(1));
            Assert.That(result.Trades.Count, Is.EqualTo(3));
            Assert.That(result.Trades.Any(t => t.Timestamp == ts[2]), Is.True);
        }
    }

    [Test]
    public async Task RunBacktestAsync_SellAbovePreviousAcceptedSell_IsAcceptedAfterNewBuy()
    {
        // Cross-cycle sell: after a fresh buy starts a new cycle, the sell anchor must reset
        // so the next exit can fire even at a higher price than the prior cycle's sell.
        var ts = new[]
        {
            T0,
            T0.AddMinutes(1),
            T0.AddMinutes(2),
            T0.AddMinutes(3),
        };

        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        SetupAdvice(ts[0], "StrongBuy", 40_000.0);
        SetupAdvice(ts[1], "FireSellStrong", 44_000.0);
        SetupAdvice(ts[2], "StrongBuy", 45_000.0);
        SetupAdvice(ts[3], "FireSellStrong", 45_500.0);

        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys, Is.EqualTo(2));
            Assert.That(result.TotalSells, Is.EqualTo(2));
            Assert.That(result.Trades.Count, Is.EqualTo(4));
            Assert.That(result.Trades.Any(t => t.Timestamp == ts[3]), Is.True);
        }
    }

    // ── Tests: FireSell setzt Tracking zurück ─────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_AfterFireSell_LowestBuyTrackingResets()
    {
        // Arrange: Buy→FireSell→Buy (höher, aber neuer Zyklus) – nach FireSell wird simLowestBuyAfterFireSell neu gesetzt
        var ts = new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2) };
        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        SetupAdvice(ts[0], "StrongBuy", 50_000.0);
        SetupAdvice(ts[1], "FireSellStrong", 53_000.0);
        SetupAdvice(ts[2], "StrongBuy", 54_000.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        // Nach dem FireSell muss der zweite Kauf als neuer lowestBuyAfterFireSell gelten
        // → Der Advisor wird beim 3. Timestamp mit lowestBuyAfterFireSell == null aufgerufen (gesetzt durch FireSell-Reset)
        // Wir prüfen indirekt: kein Fehler, korrekte Trade-Anzahl
        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.TotalBuys, Is.EqualTo(2));
            Assert.That(result.TotalSells, Is.EqualTo(1));
        }
    }

    // ── Tests: Buy-and-Hold-Berechnung ───────────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_BuyAndHold_CalculatedFromFirstToLastPrice()
    {
        // 40k → 60k → +50% Buy-and-Hold
        var ts = new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2) };
        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        SetupAdvice(ts[0], "Wait", 40_000.0);
        SetupAdvice(ts[1], "Wait", 50_000.0);
        SetupAdvice(ts[2], "Wait", 60_000.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.FirstPrice, Is.EqualTo(40_000m));
            Assert.That(result.LastPrice, Is.EqualTo(60_000m));
            Assert.That(result.BuyAndHoldPercent, Is.EqualTo(50.0).Within(0.01));
        }
    }

    // ── Tests: Timestamps werden korrekt gezählt ──────────────────────────────

    [Test]
    public async Task RunBacktestAsync_TimestampsEvaluated_MatchesTimestampCount()
    {
        // Arrange
        var ts = new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2) };
        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        foreach (var t in ts)
            SetupAdvice(t, "Wait", 45_000.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        // Assert
        Assert.That(result.TimestampsEvaluated, Is.EqualTo(3));
    }

    // ── Tests: Preis 0 wird übersprungen ─────────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_PriceZero_IsSkipped()
    {
        // Arrange
        SetupTimestamps(T0);
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "StrongBuy", 0.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.Trades, Is.Empty);
            Assert.That(result.FirstPrice, Is.Null);
        }
    }

    // ── Tests: MicroStrategy-Preise werden weitergegeben ─────────────────────

    [Test]
    public async Task RunBacktestAsync_WithMicroStrategyPrices_PassedToTradeAdvisor()
    {
        // Arrange
        var stockCandle = new StockCandle { HighPrice = 350m, LowPrice = 300m, Timestamp = T0 };
        SetupTimestamps(T0);
        _mockStockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("MSTR", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StockCandle>)new List<StockCandle> { stockCandle }.AsReadOnly());
        _mockTradeAdvisor
            .Setup(a => a.GetBitcoinTradeAdvice(
                T0, T0,
                null, null,
                350m, 300m,
                null, null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)new[] { MakeStatus("Wait", 45_000.0) });

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        // Assert
        _mockTradeAdvisor.Verify(a => a.GetBitcoinTradeAdvice(
            T0, T0, null, null, 350m, 300m, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Tests: Maximaler Gewinn / Verlust ─────────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_MaxGainAndMaxLoss_AreCorrect()
    {
        // Zyklus 1: +10%, Zyklus 2: -5%
        var ts = new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2), T0.AddMinutes(3) };
        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        SetupAdvice(ts[0], "StrongBuy", 40_000.0);
        SetupAdvice(ts[1], "FireSellStrong", 44_000.0);   // +10%
        SetupAdvice(ts[2], "StrongBuy", 44_000.0);
        SetupAdvice(ts[3], "FireSellStrong", 41_800.0);   // -5%

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(result.MaxGainPercent, Is.EqualTo(10.0).Within(0.1));
            Assert.That(result.MaxLossPercent, Is.EqualTo(-5.0).Within(0.1));
            Assert.That(result.WinRatePercent, Is.EqualTo(50.0));
        }
    }

    [Test]
    public async Task RunBacktestAsync_HardStopLossEnabled_ForcesSellWhenThresholdReached()
    {
        // Arrange – Buy at 100, then current price 90 => -10% with threshold -8% triggers forced sell.
        var t1 = T0;
        var t2 = T0.AddMinutes(1);

        SetupTimestamps(t1, t2);
        SetupMicroStrategyUnavailable();
        SetupAdvice(t1, "StrongBuy", 100.0);
        SetupAdvice(t2, "Wait", 90.0);

        _mockRiskControlService
            .Setup(r => r.ShouldTriggerHardStopLoss(100m, 90m, -8.0))
            .Returns(true);
        _mockRiskControlService
            .Setup(r => r.EvaluateHardStop(100m, 90m, It.IsAny<decimal?>(), -8.0, It.IsAny<double>()))
            .Returns(new StopLossEvaluation(true, 90m));

        var sut = new BacktestService(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions
            {
                EnableHardStopLoss = true,
                HardStopLossPercent = -8.0
            }),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions { Enabled = false }));

        // Act
        var result = await sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        var forcedSell = result.Trades.Single(t => t.TradeAdvice == "HardStopLoss");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys, Is.EqualTo(1));
            Assert.That(result.TotalSells, Is.EqualTo(1));
            Assert.That(forcedSell.ProfitPercent, Is.EqualTo(-10.0).Within(0.001));
            Assert.That(result.TotalProfitPercent, Is.EqualTo(-10.0).Within(0.01));
        }
    }

    [Test]
    public async Task RunBacktestAsync_AtrStopFiresEarlierThanHardStop_TriggersForcedSell()
    {
        // Buy at 100, drop to 95. HardStop at -8% (=92) does NOT trigger,
        // but ATR=2 with k=2 → stop=96, so ATR-stop fires at 95.
        var t1 = T0;
        var t2 = T0.AddMinutes(1);

        SetupTimestamps(t1, t2);
        SetupMicroStrategyUnavailable();
        SetupAdvice(t1, "StrongBuy", 100.0);
        SetupAdvice(t2, "Wait", 95.0);

        _mockRiskControlService
            .Setup(r => r.ShouldTriggerHardStopLoss(100m, 95m, -8.0))
            .Returns(false);
        _mockRiskControlService
            .Setup(r => r.ShouldTriggerAtrStop(100m, 95m, 2.0d, 2.0d))
            .Returns(true);
        _mockRiskControlService
            .Setup(r => r.EvaluateHardStop(100m, 95m, It.IsAny<decimal?>(), -8.0, It.IsAny<double>()))
            .Returns(new StopLossEvaluation(false, 0m));
        _mockRiskControlService
            .Setup(r => r.EvaluateAtrStop(100m, 95m, It.IsAny<decimal?>(), 2.0d, 2.0d, It.IsAny<double>()))
            .Returns(new StopLossEvaluation(true, 95m));

        _mockAtrLookupService
            .Setup(a => a.GetAtrAsync("1d", It.IsAny<DateTime>(), 14, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2.0d);

        var sut = new BacktestService(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions
            {
                EnableHardStopLoss = true,
                HardStopLossPercent = -8.0,
                EnableAtrStopLoss = true,
                AtrPeriod = 14,
                AtrMultiplier = 2.0,
                AtrInterval = "1d"
            }),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions { Enabled = false }));

        var result = await sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        var forcedSell = result.Trades.Single(t => t.TradeAdvice == "HardStopLoss");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys, Is.EqualTo(1));
            Assert.That(result.TotalSells, Is.EqualTo(1));
            Assert.That(forcedSell.ProfitPercent, Is.EqualTo(-5.0).Within(0.001));
            _mockAtrLookupService.Verify(
                a => a.GetAtrAsync("1d", It.IsAny<DateTime>(), 14, It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }
    }

    [Test]
    public async Task RunBacktestAsync_DeepCycleLowOverride_ExecutesBuyAtNextDrawdownCandleOpen()
    {
        var signalTs = T0;
        var nextOpenTs = T0.AddHours(1);

        SetupTimestamps(signalTs);
        SetupMicroStrategyUnavailable();
        SetupAdvice(signalTs, "StrongBuy", 40_000.0);

        // Bear-market map: 200 "1d" candles ending 2024-12-31; last close (1000) far below SMA200 (~49755) → isBear=true
        var baseDate = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var dailyCandles = Enumerable.Range(0, 200)
            .Select(i =>
            {
                var closeTime = baseDate.AddDays(i);
                double close = i < 199 ? 50_000.0 : 1_000.0;
                return new OhlcvCandle(closeTime.AddDays(-1), close, close, close, close, 0, closeTime);
            })
            .ToList<OhlcvCandle>();
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)dailyCandles.AsReadOnly());

        // Deep-cycle-low map: "1h" candles with a historical ATH candle (High=160000) so that
        // the drawdown at signal date is (39200-160000)/160000 = -75.5% ≤ -75% → isDeepLow=true
        // TryResolveNextCandleOpen still finds nextOpenTs candle (Open=39500) ✓
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1h", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>
            {
                new(T0.AddDays(-100), 100_000, 160_000, 100_000, 150_000, 0, T0.AddDays(-99)),
                new(signalTs.AddMinutes(-30), 39_800, 40_100, 39_700, 40_000, 0, signalTs),
                new(nextOpenTs, 39_500, 39_900, 39_200, 39_650, 0, nextOpenTs.AddHours(1))
            });

        var sut = new BacktestService(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions()),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                Enabled = true,
                Interval = "1d",
                MovingAveragePeriod = 200,
                AthDrawdownOverrideEnabled = true,
                AthDrawdownOverrideThresholdPercent = 75.0,
                AthDrawdownInterval = "1h"
            }));

        var result = await sut.RunBacktestAsync(T0, T0.AddHours(2), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys, Is.EqualTo(1));
            Assert.That(result.Trades[0].Timestamp, Is.EqualTo(nextOpenTs));
            Assert.That(result.Trades[0].Price, Is.EqualTo(39_500m));
        }
    }

    [Test]
    public async Task RunBacktestAsync_BuySignalWhenCycleTopZoneActive_BlocksBuy()
    {
        // ADR-008: When the CycleTopSellZone is active at the time of a buy signal,
        // the buy must be BLOCKED (tight-loop guard). Previously, the buy would execute
        // and immediately be followed by a DistributionSell on the same candle.
        var t1 = T0;
        var t2 = T0.AddMinutes(1);
        SetupTimestamps(t1, t2);
        SetupMicroStrategyUnavailable();
        SetupAdvice(t1, "StrongBuy", 40_000.0);
        SetupAdvice(t2, "Wait", 42_000.0);

        // Candles configured so drawup >= 350% and ATH-proximity >= 90% → overvalued = true for all dates.
        // Closed-candle convention: zone[T0] uses ordered[toIdx-1].High = candle 0's High.
        // Both candles are expensive so latestHigh (= candle 0, yesterday's closed candle) triggers the zone.
        var overvaluedCandles = (IReadOnlyList<OhlcvCandle>)new[]
        {
            new OhlcvCandle(OpenTime: T0.AddDays(-2), Open: 9_000, High: 46_000, Low: 9_000, Close: 46_000, Volume: 0, CloseTime: T0.AddDays(-1)),
            new OhlcvCandle(OpenTime: T0.AddHours(-2), Open: 45_000, High: 46_000, Low: 44_000, Close: 46_000, Volume: 0, CloseTime: T0),
        }.ToList().AsReadOnly();
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(overvaluedCandles);

        var sut = new BacktestService(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions()),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                Enabled = false,
                CycleTopSellZoneEnabled = true,
                CycleTopSellZoneInterval = "1d",
                CycleTopSellZoneLookbackDays = 1460,
                CycleTopSellZoneMinDrawupPercent = 350.0,
                CycleTopSellZoneMinAthProximityPercent = 90.0,
                CycleTopSellZoneMinMonthsAfterHalving = 0,
                CycleTopSellZoneMaxMonthsAfterHalving = 0,
            }));

        var result = await sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        // Buy must be blocked — no position should be opened while zone is active
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys, Is.EqualTo(0), "Buy must be blocked when CycleTopSellZone is active");
            Assert.That(result.TotalSells, Is.EqualTo(0), "No sell without a prior buy");
        }
    }

    [Test]
    public async Task RunBacktestAsync_CycleTopConfirmationDays_BlocksSingleDaySpike()
    {
        // ADR-008: Confirmation-Window requires the raw zone to have been armed N days
        // earlier with no new ATH. Setup1dCandlesCycleTopZone arms the raw zone only at
        // T0+1day (a single-day price spike) — with ConfirmationDays=14 the
        // post-process must clear the zone at T0+1day, suppressing the DistributionSell.
        SetupTimestampsWithPrices(
            (T0,            10_000.0),
            (T0.AddDays(1), 10_000.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000);
        Setup1dCandlesCycleTopZone();

        var sut = BuildBacktestServiceWithConfirmation(confirmationDays: 14, athTolerancePercent: 1.0);
        var result = await sut.RunBacktestAsync(
            T0, T0.AddDays(2), CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            macroOnlyMode: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1), "DeepCycleLow StrongBuy at T0");
            Assert.That(result.TotalSells, Is.EqualTo(0), "Sell at T0+1day blocked by confirmation-window: no arming at T0-13d");
        }
    }

    [Test]
    public async Task RunBacktestAsync_CycleTopConfirmationDays_AllowsSell_WhenPreviouslyArmedWithStableAth()
    {
        // ADR-008: When the raw zone was also armed N days earlier AND the cycle ATH has
        // not grown beyond tolerance, the confirmation-window allows the DistributionSell.
        // Strategy: open the position on T0 with the raw zone NOT YET armed (cheap
        // closed-candle yesterday), then a single expensive candle at T0 arms the zone
        // at T0+1day. To make confirmation pass at T0+1day, we need the raw zone to also
        // be armed at T0+1day - 14d = T0-13d. We engineer that by inserting an earlier
        // expensive spike day exactly 14 days prior so the raw zone arms at T0-13d
        // (toIdx-1 = the spike candle) with the SAME cycle ATH used at T0+1day.
        SetupTimestampsWithPrices(
            (T0,            10_000.0),
            (T0.AddDays(1), 10_000.0));
        SetupMicroStrategyUnavailable();
        // Cycle-low fires at T0 (−90 % drawdown) → StrongBuy override path opens position.
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000);

        // Build 1d-candles: cheap baseline (low=250) + one expensive spike at T0-14d
        // and one expensive spike at T0-1d. Closed-candle convention: zone[date] reads
        // ordered[toIdx-1].High (yesterday's closed candle, since today's daily candle
        // is excluded as forward-looking).
        //   • For date = T0-13d (toIdx=86) → reads ordered[85] (CloseTime=T0-14d) → 10_000 → armed.
        //   • For date = T0+1day (toIdx=99) → reads ordered[98] (CloseTime=T0-1d) → 10_000 → armed.
        //   • For date = T0 (toIdx=98) → reads ordered[97] (CloseTime=T0-2d) → 500 → NOT armed
        //     (so the StrongBuy override at T0 is not blocked by the buy-side zone gate).
        // Cycle ATH at both armed dates = 10_000 → tolerance check passes.
        const int totalDays = 100;
        var spikeAtMinus14 = totalDays - 14 - 1;   // i=85, CloseTime=T0.AddDays(-14)
        var spikeAtMinus1  = totalDays - 2;        // i=98, CloseTime=T0.AddDays(-1)
        var candles = Enumerable.Range(0, totalDays)
            .Select(i =>
            {
                var closeTime = T0.AddDays(-totalDays + i + 1);
                var isExpensive = i == spikeAtMinus14 || i == spikeAtMinus1;
                return isExpensive
                    ? new OhlcvCandle(closeTime.AddDays(-1), 9_000, 10_000, 9_000, 9_500, 0, closeTime)
                    : new OhlcvCandle(closeTime.AddDays(-1), 250, 500, 250, 375, 0, closeTime);
            })
            .ToList<OhlcvCandle>()
            .AsReadOnly();
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)candles);
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesRangeAsync("1d", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>().AsReadOnly());

        var sut = BuildBacktestServiceWithConfirmation(confirmationDays: 14, athTolerancePercent: 1.0);
        var result = await sut.RunBacktestAsync(
            T0, T0.AddDays(2), CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            macroOnlyMode: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1), "DeepCycleLow StrongBuy at T0 opens position");
            Assert.That(result.TotalSells, Is.EqualTo(1), "Sell at T0+1day allowed: zone armed at T0-13d, ATH unchanged");
        }
    }

    [Test]
    public async Task RunBacktestAsync_CycleTopConfirmationDays_AllowsSell_OnRisingAth_WhenToleranceDisabled()
    {
        // ADR-008 (decoupled, 2026-05-15): With Tolerance <= 0, the ATH-growth check is
        // skipped — only the armed-at-both filter remains. This preserves correct top
        // sells in still-rising-ATH phases (e.g., the 2025-05-23 live case) while still
        // blocking single-day spikes.
        //
        // Setup: zone armed at T0-13d (spike at i=85, cycleAth=10_000) and at T0+1day
        // (spike at i=98, cycleAth grew to 20_000 = +100 % > strict 1 % tolerance).
        // With Tolerance=1 % → sell would be blocked. With Tolerance=0 → sell fires.
        SetupTimestampsWithPrices(
            (T0,            10_000.0),
            (T0.AddDays(1), 10_000.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000);

        const int totalDays = 100;
        var spikeAtMinus14 = totalDays - 14 - 1;   // i=85, CloseTime=T0.AddDays(-14), High=10_000
        var spikeAtMinus1  = totalDays - 2;        // i=98, CloseTime=T0.AddDays(-1), High=20_000 (rising ATH)
        var candles = Enumerable.Range(0, totalDays)
            .Select(i =>
            {
                var closeTime = T0.AddDays(-totalDays + i + 1);
                return i switch
                {
                    var x when x == spikeAtMinus14 =>
                        new OhlcvCandle(closeTime.AddDays(-1),  9_000, 10_000,  9_000,  9_500, 0, closeTime),
                    var x when x == spikeAtMinus1  =>
                        new OhlcvCandle(closeTime.AddDays(-1), 18_000, 20_000, 18_000, 19_000, 0, closeTime),
                    _ =>
                        new OhlcvCandle(closeTime.AddDays(-1),    250,    500,    250,    375, 0, closeTime),
                };
            })
            .ToList<OhlcvCandle>()
            .AsReadOnly();
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)candles);
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesRangeAsync("1d", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>().AsReadOnly());

        var sut = BuildBacktestServiceWithConfirmation(confirmationDays: 14, athTolerancePercent: 0.0);
        var result = await sut.RunBacktestAsync(
            T0, T0.AddDays(2), CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            macroOnlyMode: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1), "DeepCycleLow StrongBuy at T0 opens position");
            Assert.That(result.TotalSells, Is.EqualTo(1),
                "Sell at T0+1day fires: armed at T0-13d, ATH growth ignored when tolerance=0");
        }
    }

    [TestCase(1, 1, TestName = "RunBacktestAsync_CycleTopConfirmationDays_N1_FiresAtParabolicTop_With2DayRawWindow")]
    [TestCase(2, 0, TestName = "RunBacktestAsync_CycleTopConfirmationDays_N2_MissesParabolicTop_With2DayRawWindow")]
    public async Task RunBacktestAsync_CycleTopConfirmationDays_ParabolicTopRawWindow(
        int confirmationDays, int expectedSells)
    {
        // ADR-008 v3 (2026-05-18): At parabolic cycle tops, PrecomputeCycleTopMapAsync's
        // anti-look-ahead `latestHigh = High[toIdx-1]` (yesterday-only) collapses the
        // raw-zone window to ~2 days around each ATH. N=2 confirmation requires
        // raw[d] ∧ raw[d-2] — a 3-day span — which no date in such a window satisfies.
        // N=1 requires only raw[d] ∧ raw[d-1] (2-day span) and fires correctly.
        //
        // Setup: a single spike at i=98 (closeTime=T0-1d). With the closed-candle
        // convention `latestHigh = ordered[toIdx-1].High`, this produces a strict
        // 2-day raw-zone window: armed at T0 (toIdx=99 → reads ordered[98]=spike)
        // and at T0+1d (toIdx=99 also → reads ordered[98]=spike), but NOT at T0-1d
        // (toIdx=98 → reads ordered[97]=cheap baseline).
        // Expected:
        //   d=T0    : confirm needs raw[T0-N]. T0-1d=false (both N=1, N=2) → not confirmed
        //             → StrongBuy at T0 NOT blocked by buy-side zone gate.
        //   d=T0+1d : N=1 → raw[T0]=true → SELL fires. N=2 → raw[T0-1d]=false → blocked.
        SetupTimestampsWithPrices(
            (T0,            10_000.0),
            (T0.AddDays(1), 10_000.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000);

        const int totalDays = 100;
        var spikeAtMinus1 = totalDays - 2; // i=98, closeTime=T0-1d
        var candles = Enumerable.Range(0, totalDays)
            .Select(i =>
            {
                var closeTime = T0.AddDays(-totalDays + i + 1);
                var isExpensive = i == spikeAtMinus1;
                return isExpensive
                    ? new OhlcvCandle(closeTime.AddDays(-1), 9_000, 10_000, 9_000, 9_500, 0, closeTime)
                    : new OhlcvCandle(closeTime.AddDays(-1), 250, 500, 250, 375, 0, closeTime);
            })
            .ToList<OhlcvCandle>()
            .AsReadOnly();
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)candles);
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesRangeAsync("1d", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>().AsReadOnly());

        var sut = BuildBacktestServiceWithConfirmation(confirmationDays: confirmationDays, athTolerancePercent: 0.0);
        var result = await sut.RunBacktestAsync(
            T0, T0.AddDays(2), CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            macroOnlyMode: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys, Is.EqualTo(1), "DeepCycleLow StrongBuy at T0 opens position");
            Assert.That(result.TotalSells, Is.EqualTo(expectedSells),
                $"N={confirmationDays}: expected {expectedSells} sell(s) at parabolic top with 2-day raw-zone window");
        }
    }

    private BacktestService BuildBacktestServiceWithConfirmation(int confirmationDays, double athTolerancePercent) =>
        new(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions()),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                Enabled = false,
                CycleTopSellZoneEnabled = true,
                CycleTopSellZoneInterval = "1d",
                CycleTopSellZoneLookbackDays = 1460,
                CycleTopSellZoneMinDrawupPercent = 350.0,
                CycleTopSellZoneMinAthProximityPercent = 90.0,
                CycleTopSellZoneMinMonthsAfterHalving = 0,
                CycleTopSellZoneMaxMonthsAfterHalving = 0,
                CycleTopSellZoneConfirmationDays = confirmationDays,
                CycleTopSellZoneConfirmationAthTolerancePercent = athTolerancePercent,
            }));

    // ── Tests: MSTR-Candles werden vorab geladen (Pre-Loading) ───────────────

    [Test]
    public async Task RunBacktestAsync_MstrCandlesPreLoaded_OnlySingleBulkQuery()
    {
        // Arrange – 3 Timestamps, MSTR nur 1x bulk geladen statt 6 Einzel-Queries
        var ts = new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2) };
        SetupTimestamps(ts);
        var candle = new StockCandle { HighPrice = 400m, LowPrice = 380m, Timestamp = T0.AddMinutes(-5), Symbol = "MSTR" };
        _mockStockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("MSTR", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StockCandle>)new List<StockCandle> { candle }.AsReadOnly());
        foreach (var t in ts)
            SetupAdvice(t, "Wait", 50_000.0);

        // Act
        await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        // Assert – GetStockCandlesUpToAsync genau 1x aufgerufen (nicht N mal)
        _mockStockCandleRepository.Verify(
            r => r.GetStockCandlesUpToAsync("MSTR", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RunBacktestAsync_MstrBinarySearch_FindsLatestCandleBeforeTimestamp()
    {
        // Arrange – 2 Candles: eine vor T0, eine zwischen T0 und T0+1min
        var candleOld = new StockCandle { HighPrice = 300m, LowPrice = 280m, Timestamp = T0.AddMinutes(-10) };
        var candleNew = new StockCandle { HighPrice = 350m, LowPrice = 320m, Timestamp = T0.AddSeconds(30) };
        SetupTimestamps(T0, T0.AddMinutes(1));
        _mockStockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("MSTR", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StockCandle>)new List<StockCandle> { candleOld, candleNew }.AsReadOnly());

        // T0 → candleOld (300/280), T0+1min → candleNew (350/320)
        _mockTradeAdvisor
            .Setup(a => a.GetBitcoinTradeAdvice(
                T0, T0,
                It.IsAny<decimal?>(), It.IsAny<decimal?>(),
                300m, 280m,
                It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)new[] { MakeStatus("Wait", 50_000.0) });
        _mockTradeAdvisor
            .Setup(a => a.GetBitcoinTradeAdvice(
                T0.AddMinutes(1), T0.AddMinutes(1),
                It.IsAny<decimal?>(), It.IsAny<decimal?>(),
                350m, 320m,
                It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)new[] { MakeStatus("Wait", 51_000.0) });

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        // Assert – Binärsuche findet für T0 die ältere Candle, für T0+1min die neuere
        _mockTradeAdvisor.Verify(a => a.GetBitcoinTradeAdvice(
            T0, T0, It.IsAny<decimal?>(), It.IsAny<decimal?>(),
            300m, 280m, It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockTradeAdvisor.Verify(a => a.GetBitcoinTradeAdvice(
            T0.AddMinutes(1), T0.AddMinutes(1), It.IsAny<decimal?>(), It.IsAny<decimal?>(),
            350m, 320m, It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Tests: Binärsuche Randfälle ──────────────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_MstrAllCandlesAfterTimestamp_ReturnsNullPrices()
    {
        // Arrange – alle Candles liegen nach dem Timestamp → kein Match
        var futureCandle = new StockCandle { HighPrice = 500m, LowPrice = 480m, Timestamp = T0.AddHours(10) };
        SetupTimestamps(T0);
        _mockStockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("MSTR", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StockCandle>)new List<StockCandle> { futureCandle }.AsReadOnly());
        // Advisor wird mit null/null MSTR-Preisen aufgerufen
        _mockTradeAdvisor
            .Setup(a => a.GetBitcoinTradeAdvice(
                T0, T0, null, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)new[] { MakeStatus("Wait", 50_000.0) });

        // Act
        await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        // Assert – null/null übergeben da keine passende Candle gefunden
        _mockTradeAdvisor.Verify(a => a.GetBitcoinTradeAdvice(
            T0, T0, null, null, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunBacktestAsync_MstrExactTimestampMatch_ReturnsThatCandle()
    {
        // Arrange – Candle mit exakt gleichem Timestamp wie der Backtest-Punkt
        var exactCandle = new StockCandle { HighPrice = 420m, LowPrice = 400m, Timestamp = T0 };
        SetupTimestamps(T0);
        _mockStockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("MSTR", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StockCandle>)new List<StockCandle> { exactCandle }.AsReadOnly());
        _mockTradeAdvisor
            .Setup(a => a.GetBitcoinTradeAdvice(
                T0, T0, null, null, 420m, 400m, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)new[] { MakeStatus("Wait", 50_000.0) });

        // Act
        await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        // Assert
        _mockTradeAdvisor.Verify(a => a.GetBitcoinTradeAdvice(
            T0, T0, null, null, 420m, 400m, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunBacktestAsync_MstrMultipleCandles_FindsNearestBeforeEachTimestamp()
    {
        // Arrange – 3 Candles sortiert, 2 Timestamps
        var c1 = new StockCandle { HighPrice = 100m, LowPrice = 90m, Timestamp = T0.AddMinutes(-20) };
        var c2 = new StockCandle { HighPrice = 200m, LowPrice = 180m, Timestamp = T0.AddMinutes(-5) };
        var c3 = new StockCandle { HighPrice = 300m, LowPrice = 270m, Timestamp = T0.AddSeconds(45) };
        SetupTimestamps(T0, T0.AddMinutes(1));
        _mockStockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("MSTR", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StockCandle>)new List<StockCandle> { c1, c2, c3 }.AsReadOnly());
        // T0 → c2 (200/180), T0+1min → c3 (300/270)
        _mockTradeAdvisor
            .Setup(a => a.GetBitcoinTradeAdvice(
                T0, T0, It.IsAny<decimal?>(), It.IsAny<decimal?>(),
                200m, 180m, It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)new[] { MakeStatus("Wait", 50_000.0) });
        _mockTradeAdvisor
            .Setup(a => a.GetBitcoinTradeAdvice(
                T0.AddMinutes(1), T0.AddMinutes(1), It.IsAny<decimal?>(), It.IsAny<decimal?>(),
                300m, 270m, It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)new[] { MakeStatus("Wait", 51_000.0) });

        // Act
        await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        // Assert
        _mockTradeAdvisor.Verify(a => a.GetBitcoinTradeAdvice(
            T0, T0, It.IsAny<decimal?>(), It.IsAny<decimal?>(),
            200m, 180m, It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockTradeAdvisor.Verify(a => a.GetBitcoinTradeAdvice(
            T0.AddMinutes(1), T0.AddMinutes(1), It.IsAny<decimal?>(), It.IsAny<decimal?>(),
            300m, 270m, It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunBacktestAsync_MstrSingleCandleUsedForAllTimestamps()
    {
        // Arrange – 1 Candle vor allen Timestamps → dieselbe Candle wird für alle benutzt
        var candle = new StockCandle { HighPrice = 150m, LowPrice = 130m, Timestamp = T0.AddHours(-1) };
        var ts = new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2) };
        SetupTimestamps(ts);
        _mockStockCandleRepository
            .Setup(r => r.GetStockCandlesUpToAsync("MSTR", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StockCandle>)new List<StockCandle> { candle }.AsReadOnly());
        foreach (var t in ts)
        {
            _mockTradeAdvisor
                .Setup(a => a.GetBitcoinTradeAdvice(
                    t, t, It.IsAny<decimal?>(), It.IsAny<decimal?>(),
                    150m, 130m, It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)new[] { MakeStatus("Wait", 50_000.0) });
        }

        // Act
        await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        // Assert – alle 3 Timestamps bekommen dieselbe Candle
        foreach (var t in ts)
        {
            _mockTradeAdvisor.Verify(a => a.GetBitcoinTradeAdvice(
                t, t, It.IsAny<decimal?>(), It.IsAny<decimal?>(),
                150m, 130m, It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    // ── Tests: Leere Ergebnisse vom Advisor ──────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_AdvisorReturnsEmpty_TimestampIsSkipped()
    {
        // Arrange
        SetupTimestamps(T0, T0.AddMinutes(1));
        SetupMicroStrategyUnavailable();
        // T0 gibt leere Liste zurück, T0+1 liefert normalen Trade
        _mockTradeAdvisor
            .Setup(a => a.GetBitcoinTradeAdvice(
                T0, T0, It.IsAny<decimal?>(), It.IsAny<decimal?>(),
                It.IsAny<decimal?>(), It.IsAny<decimal?>(),
                It.IsAny<BuyActionLog?>(), It.IsAny<BuyActionLog?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<BitcoinTradeStatus>)Array.Empty<BitcoinTradeStatus>());
        SetupAdvice(T0.AddMinutes(1), "StrongBuy", 42_000.0);

        // Act
        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // Assert – nur der zweite Timestamp zählt
            Assert.That(result.TotalBuys, Is.EqualTo(1));
            Assert.That(result.FirstPrice, Is.EqualTo(42_000m));
        }
    }

    // ── Tests: Re-Entry Cooldown ─────────────────────────────────────────────

    [Test]
    public async Task RunBacktestAsync_WithActiveCooldown_BlocksBuyImmediatelyAfterSell()
    {
        // Arrange: Buy at T0, Sell at T1, Buy attempt at T2 (inside cooldown), Buy succeeds at T3 (outside)
        var t1 = T0.AddHours(10);
        var t2 = T0.AddHours(11);   // 1h after sell → inside 24h cooldown
        var t3 = T0.AddHours(35);   // 25h after sell → outside 24h cooldown

        SetupTimestamps(T0, t1, t2, t3);
        SetupMicroStrategyUnavailable();

        SetupAdvice(T0,  "StrongBuy",  40_000.0);
        SetupAdvice(t1,  "FireSell",   42_000.0);
        SetupAdvice(t2,  "StrongBuy",  41_000.0); // should be blocked by cooldown
        SetupAdvice(t3,  "StrongBuy",  41_500.0); // should pass

        _sut = new BacktestService(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions { ReEntryCooldownHours = 24 }),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions { Enabled = false }));

        // Act
        var result = await _sut.RunBacktestAsync(T0, t3.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys, Is.EqualTo(2),   "two buys: T0 and t3");
            Assert.That(result.TotalSells, Is.EqualTo(1),  "one sell: t1");
        }
    }

    [Test]
    public async Task RunBacktestAsync_WithCooldownDisabled_AllowsBuyImmediatelyAfterSell()
    {
        // With cooldown=0, the buy at t2 should be allowed (no cooldown gate).
        var t1 = T0.AddHours(10);
        var t2 = T0.AddHours(11);

        SetupTimestamps(T0, t1, t2);
        SetupMicroStrategyUnavailable();

        SetupAdvice(T0, "StrongBuy", 40_000.0);
        SetupAdvice(t1, "FireSell",  42_000.0);
        SetupAdvice(t2, "StrongBuy", 41_000.0);

        // Default _sut has ReEntryCooldownHours=0

        var result = await _sut.RunBacktestAsync(T0, t2.AddHours(1), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(2));
            Assert.That(result.TotalSells, Is.EqualTo(1));
        }
    }

    // ── Tests: ATH-Proximity Buy-Block Gate (ADR-009 / ADR-016) ─────────────

    private BacktestService CreateSutWithOptions(BearMarketFilterOptions options) =>
        new BacktestService(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions()),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(options));

    private void SetupCandlesWithHighs(params (DateTime CloseTime, double High)[] entries)
    {
        var candles = entries
            .Select(e => new OhlcvCandle(e.CloseTime.AddHours(-2), 0, e.High, 0, 0, 0, e.CloseTime))
            .ToList<OhlcvCandle>();
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("2h", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)candles.AsReadOnly());
    }

    private static BearMarketFilterOptions AthProximityBlockOptions(int maxAthAgeDays = 180) =>
        new BearMarketFilterOptions
        {
            Enabled = false,
            AthProximityBuyBlockEnabled = true,
            AthProximityBuyBlockMaxPercent = 85.0,
            AthProximityBuyBlockMaxAthAgeDays = maxAthAgeDays,
        };

    [Test]
    public async Task RunBacktestAsync_AthProximityGate_BlocksBuyWhenPriceNearRecentAth()
    {
        // Price = 88k (88% of ATH 100k, above 85% threshold), ATH age = 0 days ≤ 180 → BLOCKED
        var sut = CreateSutWithOptions(AthProximityBlockOptions());
        SetupCandlesWithHighs((T0, 100_000.0));
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "StrongBuy", 88_000.0);

        var result = await sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        Assert.That(result.TotalBuys, Is.EqualTo(0), "Gate must block buy near recent ATH");
    }

    [Test]
    public async Task RunBacktestAsync_AthProximityGate_WhenOverrideMinusOne_DisablesGate()
    {
        // Override = -1 disables the gate entirely; buy must go through despite being near ATH
        var sut = CreateSutWithOptions(AthProximityBlockOptions());
        SetupCandlesWithHighs((T0, 100_000.0));
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "StrongBuy", 88_000.0);

        var result = await sut.RunBacktestAsync(
            T0, T0.AddHours(1), CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: 0.0,
            athProximityBuyBlockMaxAthAgeDays: -1);

        Assert.That(result.TotalBuys, Is.EqualTo(1), "Gate must be fully disabled with override -1");
    }

    [Test]
    public async Task RunBacktestAsync_AthProximityGate_WhenAthOlderThanMaxAgeDays_AllowsBuy()
    {
        // ATH set 200 days ago → age 200 > 180 (appsettings default) → gate inactive → buy allowed
        var athTime = T0.AddDays(-200);
        var sut = CreateSutWithOptions(AthProximityBlockOptions(maxAthAgeDays: 180));
        SetupCandlesWithHighs((athTime, 100_000.0), (T0, 50_000.0));
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "StrongBuy", 88_000.0);

        var result = await sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        Assert.That(result.TotalBuys, Is.EqualTo(1), "Buy allowed when ATH is older than MaxAthAgeDays");
    }

    [Test]
    public async Task RunBacktestAsync_AthProximityGate_WhenOverrideShorterThanAthAge_AllowsBuy()
    {
        // ATH set 100 days ago; appsettings=180 would block (100 ≤ 180), override=90 would not (100 > 90)
        var athTime = T0.AddDays(-100);
        var sut = CreateSutWithOptions(AthProximityBlockOptions(maxAthAgeDays: 180));
        SetupCandlesWithHighs((athTime, 100_000.0), (T0, 50_000.0));
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "StrongBuy", 88_000.0);

        var result = await sut.RunBacktestAsync(
            T0, T0.AddHours(1), CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: 0.0,
            athProximityBuyBlockMaxAthAgeDays: 90);

        Assert.That(result.TotalBuys, Is.EqualTo(1), "Override shorter than ATH age → gate inactive → buy allowed");
    }

    // ── Tests: Risk-adjusted metrics (ADR-016) ───────────────────────────────

    [Test]
    public async Task RunBacktestAsync_CompoundedProfit_IsGeometricProduct()
    {
        // Trade 1: +10%, Trade 2: +10% → compounded = (1.1 × 1.1 − 1) × 100 = +21%
        var ts = new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2), T0.AddMinutes(3) };
        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        SetupAdvice(ts[0], "StrongBuy",     40_000.0);
        SetupAdvice(ts[1], "FireSellStrong", 44_000.0); // +10%
        SetupAdvice(ts[2], "StrongBuy",     44_000.0);
        SetupAdvice(ts[3], "FireSellStrong", 48_400.0); // +10%

        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        Assert.That(result.CompoundedProfitPercent, Is.EqualTo(21.0).Within(0.1));
    }

    [Test]
    public async Task RunBacktestAsync_CompoundedProfit_IsNull_WhenNoClosedTrades()
    {
        SetupTimestamps(T0);
        SetupMicroStrategyUnavailable();
        SetupAdvice(T0, "StrongBuy", 40_000.0);

        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        Assert.That(result.CompoundedProfitPercent, Is.Null);
    }

    [Test]
    public async Task RunBacktestAsync_TimeInMarket_IsZero_WhenNoTrades()
    {
        var ts = new[] { T0, T0.AddHours(1) };
        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        SetupAdvice(ts[0], "Wait", 40_000.0);
        SetupAdvice(ts[1], "Wait", 42_000.0);

        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(2), CancellationToken.None);

        Assert.That(result.TimeInMarketPercent, Is.EqualTo(0.0).Within(1.0));
    }

    [Test]
    public async Task RunBacktestAsync_MaxDrawdown_IsNonNegative_AfterPriceDropInPosition()
    {
        // Buy at 100, price drops to 80 (−20%), then recovers to 90.
        // Strategy max drawdown must be ≥ 20%.
        var ts = new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2) };
        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        SetupAdvice(ts[0], "StrongBuy", 100.0);
        SetupAdvice(ts[1], "Wait",       80.0);
        SetupAdvice(ts[2], "Wait",       90.0);

        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        Assert.That(result.MaxDrawdownPercent, Is.GreaterThanOrEqualTo(18.0));
    }

    [Test]
    public async Task RunBacktestAsync_HodlMaxDrawdown_ReflectsBtcPeakToTrough()
    {
        // Prices: 100 → 120 (peak) → 60 (trough) = −50% HODL drawdown.
        var ts = new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2) };
        SetupTimestamps(ts);
        SetupMicroStrategyUnavailable();
        SetupAdvice(ts[0], "Wait", 100.0);
        SetupAdvice(ts[1], "Wait", 120.0);
        SetupAdvice(ts[2], "Wait",  60.0);

        var result = await _sut.RunBacktestAsync(T0, T0.AddHours(1), CancellationToken.None);

        Assert.That(result.HodlMaxDrawdownPercent, Is.EqualTo(50.0).Within(0.1));
    }

    // ── Tests: macroOnlyMode ─────────────────────────────────────────────────

    /// <summary>
    /// Sets up 2h candles with specific Close prices for macroOnlyMode tests.
    /// macroOnlyMode skips the trade advisor and uses candle Close prices directly.
    /// </summary>
    private void SetupTimestampsWithPrices(params (DateTime CloseTime, double Close)[] entries) =>
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("2h", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)entries
                .Select(e => new OhlcvCandle(e.CloseTime.AddHours(-2), 0, 0, 0, e.Close, 0, e.CloseTime))
                .ToList()
                .AsReadOnly());

    /// <summary>
    /// Creates 1h candles for deep-cycle-low detection.
    /// The ATH candle (High=<paramref name="athHigh"/>) is placed 2 years before T0;
    /// the current candle (Low=<paramref name="currentLow"/>) is at T0.
    /// DrawdownPct = (currentLow − athHigh) / athHigh × 100.
    /// Default threshold is −75 %; pass currentLow &lt; 0.25 × athHigh to trigger.
    /// </summary>
    private void Setup1hCandles(double athHigh, double currentLow) =>
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1h", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>
            {
                // ATH candle well in the past — establishes the running ATH via High
                new OhlcvCandle(T0.AddYears(-2).AddHours(-1), athHigh, athHigh, athHigh, athHigh, 0, T0.AddYears(-2)),
                // Current candle at T0 — latestLow used for drawdown calculation
                new OhlcvCandle(T0.AddHours(-1), currentLow, currentLow, currentLow, currentLow, 0, T0),
            }.AsReadOnly());

    /// <summary>
    /// Creates 350 daily candles that trigger Pi Cycle Top (SMA(111) ≥ SMA(350)×2) on
    /// <c>T0+1day</c> (the sell date) but NOT on <c>T0</c> (the buy date).
    /// At T0 only 349 candles exist → toIdx=348 &lt; 349 → false (not enough for SMA-350).
    /// At T0+1day all 350 candles exist → SMA(111)=100 ≥ 2×SMA(350)≈64.8 → true.
    /// CycleTopZone stays false: High/Low = 0 → valid=false.
    /// </summary>
    private void Setup1dCandlesPiCycleTop() =>
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)Enumerable.Range(0, 350)
                .Select(i =>
                {
                    // i=349 → T0+1day (sell date); i=348 → T0 (buy date)
                    var closeTime = T0.AddDays(-349 + i + 1);
                    var close = i < 239 ? 1.0 : 100.0;
                    // High/Low = 0 → CycleTopZone valid=false → returns false
                    return new OhlcvCandle(closeTime.AddDays(-1), 0, 0, 0, close, 0, closeTime);
                })
                .ToList<OhlcvCandle>()
                .AsReadOnly());

    /// <summary>
    /// Creates 100 daily candles that trigger CycleTopZone on <c>T0+1day</c> (sell date)
    /// but NOT on <c>T0</c> (buy date). Pi Cycle Top stays false (only 100 candles &lt; 350).
    /// Closed-candle convention: zone[date] uses ordered[toIdx-1].High (yesterday's closed candle).
    /// At T0 (toIdx=98): latestHigh = ordered[97].High = 500 (cheap) → drawup=100 % &lt; 350 % → false.
    /// At T0+1day (toIdx=99): latestHigh = ordered[98].High = 10 000 (expensive, closed at T0)
    ///   → cycleLow=250, cycleAth=10 000, drawup=3 900 % ≥ 350 % ✓  athProx=100 % ≥ 90 % ✓ → true.
    /// </summary>
    private void Setup1dCandlesCycleTopZone() =>
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)Enumerable.Range(0, 100)
                .Select(i =>
                {
                    // i=98 → T0 (expensive, yesterday's closed candle for T0+1day sell date)
                    // i=99 → T0+1day (toIdx for sell date; latestHigh uses i=98, not i=99)
                    var closeTime = T0.AddDays(-99 + i + 1);
                    return i < 98
                        ? new OhlcvCandle(closeTime.AddDays(-1), 250, 500, 250, 375, 0, closeTime)   // cheap
                        : new OhlcvCandle(closeTime.AddDays(-1), 9_000, 10_000, 9_000, 9_500, 0, closeTime); // expensive
                })
                .ToList<OhlcvCandle>()
                .AsReadOnly());

    /// <summary>
    /// Creates an empty 1d candle list — neither Pi Cycle Top nor CycleTopZone fires.
    /// </summary>
    private void Setup1dCandlesEmpty() =>
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>().AsReadOnly());

    /// <summary>
    /// Calls RunBacktestAsync with macroOnlyMode=true and all other parameters at defaults.
    /// </summary>
    private Task<BacktestSummary> RunMacroOnlyAsync(DateTime start, DateTime end) =>
        _sut.RunBacktestAsync(
            start, end, CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            macroOnlyMode: true);

    [Test]
    public async Task RunBacktestAsync_MacroOnly_NoTrade_WhenNeitherSignalActive()
    {
        // Deep-cycle-low NOT triggered (only −5 % drawdown from ATH).
        // 1d candles empty → Pi Cycle Top and CycleTopZone both return false.
        // All timestamps produce "Wait" → no trades.
        SetupTimestampsWithPrices((T0, 50_000.0), (T0.AddMinutes(1), 50_000.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 95_000); // −5 % drawdown
        Setup1dCandlesEmpty();

        var result = await RunMacroOnlyAsync(T0, T0.AddHours(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.Zero);
            Assert.That(result.TotalSells, Is.Zero);
        }
    }

    [Test]
    public async Task RunBacktestAsync_MacroOnly_Buy_WhenDeepCycleLow()
    {
        // Deep-cycle-low triggered: ATH=100 000, current low=10 000 → −90 % drawdown.
        // No position open → advice overridden to "StrongBuy".
        // No sell signal active → position stays open, TotalSells=0.
        SetupTimestampsWithPrices((T0, 10_000.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000); // −90 % drawdown
        Setup1dCandlesEmpty();

        var result = await RunMacroOnlyAsync(T0, T0.AddHours(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1));
            Assert.That(result.TotalSells, Is.Zero);
        }
    }

    [Test]
    public async Task RunBacktestAsync_MacroOnly_PiCycleTop_DoesNotSell()
    {
        // Pi Cycle Top is excluded from macroOnly sell logic (fires too early in real cycles).
        // T0 (buy date):      deep-cycle-low fires → StrongBuy, position opens.
        // T0+1day (sell date): Pi Cycle Top map = true, but macroOnly only uses CycleTopZone.
        //                     CycleTopZone stays false (High/Low=0 → valid=false).
        //                     → advice remains "Wait", position stays open, TotalSells=0.
        SetupTimestampsWithPrices((T0, 10_000.0), (T0.AddDays(1), 10_000.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000); // −90 % drawdown
        Setup1dCandlesPiCycleTop();

        var result = await RunMacroOnlyAsync(T0, T0.AddDays(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1));
            Assert.That(result.TotalSells, Is.Zero);
        }
    }

    [Test]
    public async Task RunBacktestAsync_MacroOnly_Sell_WhenCycleTopOvervalued()
    {
        // T0 (buy date):      deep-cycle-low fires (−90 %) + no position → StrongBuy.
        //                     CycleTopZone = false at T0 (all 99 candles in cheap phase;
        //                     latestHigh=500, drawup=100 % < 350 %)
        //                     → ADR-008 tight-loop guard does not block the buy.
        // T0+1day (sell date): CycleTopZone = true (expensive candle appears;
        //                     cycleLow=250, cycleAth=10 000, drawup=3 900 %, athProx=100 %)
        //                     + position open → DistributionSell closes position.
        // Pi Cycle Top stays false: only 100 1d candles, < 350 required.
        SetupTimestampsWithPrices((T0, 10_000.0), (T0.AddDays(1), 10_000.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000); // −90 % drawdown
        Setup1dCandlesCycleTopZone();

        var result = await RunMacroOnlyAsync(T0, T0.AddDays(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1));
            Assert.That(result.TotalSells, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task RunBacktestAsync_MacroOnly_TrailingStop_DoesNotFire()
    {
        // In macroOnly mode all stop losses (ATR, Hard, Trailing) are suppressed so positions
        // ride the full macro cycle. Verify that even when EnableTrailingStop=true and
        // EvaluateTrailingStop would return Triggered=true, no sell is recorded.
        //
        // T0: deep-cycle-low fires (−90 %) → StrongBuy at 10 000.
        // T0+1h: price = 5 000 — a −50 % drop that would normally fire the trailing stop.
        //        In macroOnly mode the stop is bypassed → position stays open, TotalSells=0.
        SetupTimestampsWithPrices((T0, 10_000.0), (T0.AddHours(1), 5_000.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000); // −90 % drawdown → buy fires
        Setup1dCandlesEmpty();

        // Trailing stop that would fire immediately (activation=0 %, drop=10 %).
        _mockRiskControlService
            .Setup(r => r.EvaluateTrailingStop(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()))
            .Returns(new StopLossEvaluation(true, 4_500m));

        var sutWithTrailingStop = new BacktestService(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions
            {
                EnableTrailingStop = true,
                TrailingStopActivationPercent = 0.0,
                TrailingStopPercent = 10.0,
            }),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions { Enabled = false }));

        var result = await sutWithTrailingStop.RunBacktestAsync(
            T0, T0.AddHours(2), CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            macroOnlyMode: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1));
            Assert.That(result.TotalSells, Is.Zero, "Trailing stop must not fire in macroOnlyMode");
        }

        _mockRiskControlService.Verify(
            r => r.EvaluateTrailingStop(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()),
            Times.Never,
            "EvaluateTrailingStop must never be called in macroOnlyMode");
    }

    [Test]
    public async Task RunBacktestAsync_MacroOnly_NoAthBreakoutReEntry_AfterDistSell()
    {
        // macroOnly uses ONLY DeepCycleLow (buy) and CycleTopZone (sell).
        // After a DistSell there is no AthBreakoutBuy re-entry; the system waits for the
        // next genuine deep cycle low.
        //
        // T0      : deep-cycle-low fires (−90 %); no open position → StrongBuy at 10 000.
        // T0+1day : CycleTopZone active + position open → DistributionSell.
        // T0+2day : no deep-cycle-low (1h Low = 95 000, only −5 % from ATH), cycleTopZone
        //           still active, no position → Wait (no re-entry).
        // Expected: TotalBuys = 1, TotalSells = 1.
        SetupTimestampsWithPrices(
            (T0,            10_000.0),
            (T0.AddDays(1), 10_000.0),
            (T0.AddDays(2), 10_200.0));

        SetupMicroStrategyUnavailable();

        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1h", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>
            {
                new OhlcvCandle(T0.AddYears(-2).AddHours(-1), 100_000, 100_000, 100_000, 100_000, 0, T0.AddYears(-2)),
                new OhlcvCandle(T0.AddHours(-1), 10_000, 10_000, 10_000, 10_000, 0, T0),
                new OhlcvCandle(T0.AddDays(2).AddHours(-1), 95_000, 95_000, 95_000, 95_000, 0, T0.AddDays(2)),
            }.AsReadOnly());

        Setup1dCandlesCycleTopZone();

        var result = await RunMacroOnlyAsync(T0, T0.AddDays(3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1), "Only the initial StrongBuy — no AthBreakoutBuy re-entry");
            Assert.That(result.TotalSells, Is.EqualTo(1), "DistributionSell at T0+1day");
        }
    }

    [Test]
    public async Task RunBacktestAsync_MacroOnly_WaitsForDeepCycleLow_AfterMultipleCycleTopSells()
    {
        // macroOnly re-entry is ONLY via DeepCycleLow. Multiple CycleTop sells without a
        // new deep cycle low result in one buy and one sell only.
        //
        // T0      : deep-cycle-low (−90 %) → StrongBuy at 10 000.
        // T0+1day : CycleTopZone + open position → DistSell.
        // T0+2day .. T0+4day : cycleTopZone active, no deep-cycle-low, no position → Wait.
        // Expected: TotalBuys = 1, TotalSells = 1.
        SetupTimestampsWithPrices(
            (T0,            10_000.0),
            (T0.AddDays(1), 10_000.0),
            (T0.AddDays(2), 10_200.0),
            (T0.AddDays(3), 10_200.0),
            (T0.AddDays(4), 10_200.0));

        SetupMicroStrategyUnavailable();

        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1h", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>
            {
                new OhlcvCandle(T0.AddYears(-2).AddHours(-1), 100_000, 100_000, 100_000, 100_000, 0, T0.AddYears(-2)),
                new OhlcvCandle(T0.AddHours(-1), 10_000, 10_000, 10_000, 10_000, 0, T0),
                new OhlcvCandle(T0.AddDays(2).AddHours(-1), 95_000, 95_000, 95_000, 95_000, 0, T0.AddDays(2)),
            }.AsReadOnly());

        Setup1dCandlesCycleTopZone();

        var result = await RunMacroOnlyAsync(T0, T0.AddDays(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1), "Only initial StrongBuy — no re-entry without a new deep cycle low");
            Assert.That(result.TotalSells, Is.EqualTo(1), "DistSell at T0+1day only");
        }
    }

    [Test]
    public async Task RunBacktestAsync_MacroOnly_CycleTopForcedSell_BypassesSmaExitDebounce()
    {
        // Fix A: CycleTop forced sells must bypass the SMA-hold exit gate so they fire at
        // bull-run peaks even when BTC is above the 200d-SMA. Without the bypass, any
        // smaExitDebounceDays > 0 would suppress ALL macroOnly DistributionSells because
        // bull peaks are always above the 200d-SMA.
        //
        // The 100 1d candles are insufficient to compute SMA-200 → all dates in
        // dailyBelowSma200 return absent (= not below) → without Fix A, belowLongEnough=false
        // → sell suppressed. With Fix A (cycleTopForcedSell bypasses SmaExitDebounce): fires.
        //
        // T0      : deep-cycle-low (−90 %) → StrongBuy. CycleTopZone = false at T0.
        // T0+1day : CycleTopZone active + position open → DistributionSell.
        //           smaExitDebounceDays = 7: gate bypassed because cycleTopForcedSell = true.
        // Expected: TotalBuys = 1, TotalSells = 1.
        SetupTimestampsWithPrices((T0, 10_000.0), (T0.AddDays(1), 10_000.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000);
        Setup1dCandlesCycleTopZone();

        // SMA-200 lookup requires GetCandlesRangeAsync("1d"). Return empty so all dates
        // are absent in dailyBelowSma200 (= not below SMA), simulating a bull-run state
        // where every date fails the belowLongEnough check without the bypass.
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesRangeAsync("1d", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)new List<OhlcvCandle>().AsReadOnly());

        var result = await _sut.RunBacktestAsync(
            T0, T0.AddDays(2), CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            smaExitDebounceDays: 7,
            macroOnlyMode: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1));
            Assert.That(result.TotalSells, Is.EqualTo(1), "CycleTop forced sell must bypass SmaExitDebounce");
        }
    }

    /// <summary>
    /// Creates 100 daily candles where CycleTopZone ATH proximity = 98.69 % (threshold-sensitive).
    /// At T0+1day (sell date): cycleLow=15 000, cycleAth=73 794, latestHigh=72 827.
    /// drawup = (72 827 − 15 000) / 15 000 × 100 = 385 % ≥ 350 % ✓
    /// athProx = 72 827 / 73 794 × 100 = 98.69 %
    ///   → fires   when CycleTopSellZoneMinAthProximityPercent ≤ 98.69 (e.g. 98.0 %)
    ///   → blocked when CycleTopSellZoneMinAthProximityPercent > 98.69 (e.g. 99.0 %)
    /// </summary>
    private void Setup1dCandlesCycleTopZoneNearAth() =>
        _mockBinanceKlineService
            .Setup(r => r.GetCandlesUpToAsync("1d", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OhlcvCandle>)Enumerable.Range(0, 100)
                .Select(i =>
                {
                    var closeTime = T0.AddDays(-99 + i + 1);
                    return i < 98
                        ? new OhlcvCandle(closeTime.AddDays(-1), 15_000, 15_000, 15_000, 15_000, 0, closeTime)
                        : i == 98
                            ? new OhlcvCandle(closeTime.AddDays(-1), 72_000, 72_827, 72_000, 72_400, 0, closeTime)
                            : new OhlcvCandle(closeTime.AddDays(-1), 73_000, 73_794, 73_000, 73_400, 0, closeTime);
                })
                .ToList<OhlcvCandle>()
                .AsReadOnly());

    [Test]
    public async Task RunBacktestAsync_MacroOnly_CycleTopZone_NoSell_WhenAthProximityBelowThreshold()
    {
        // Fix C: raising CycleTopSellZoneMinAthProximityPercent from 98 % to 99 % prevents
        // premature cycle-top exits when price is near but not at the running ATH.
        //
        // Candle setup: cycleLow=15 000, cycleAth=73 794, latestHigh=72 827
        //   → athProx = 72 827 / 73 794 × 100 = 98.69 %
        //   → drawup  = 385 % ≥ 350 % ✓
        //
        // With threshold = 99.0 %: 98.69 % < 99.0 % → zone does NOT fire → no sell.
        // (With threshold = 98.0 %: 98.69 % ≥ 98.0 % → zone fires → sell would occur.)
        //
        // T0      : deep-cycle-low (−90 %) → StrongBuy at 72 000.
        // T0+1day : CycleTopZone = false (98.69 % < threshold 99.0 %) → position held.
        // Expected: TotalBuys = 1, TotalSells = 0.
        SetupTimestampsWithPrices((T0, 72_000.0), (T0.AddDays(1), 72_827.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000);
        Setup1dCandlesCycleTopZoneNearAth();

        var sutWith99PctThreshold = new BacktestService(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions()),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                CycleTopSellZoneEnabled = true,
                CycleTopSellZoneInterval = "1d",
                CycleTopSellZoneLookbackDays = 1460,
                CycleTopSellZoneMinDrawupPercent = 350.0,
                CycleTopSellZoneMinAthProximityPercent = 99.0,
                CycleTopSellZoneMinMonthsAfterHalving = 0,
                CycleTopSellZoneMaxMonthsAfterHalving = 0,
            }));

        var result = await sutWith99PctThreshold.RunBacktestAsync(
            T0, T0.AddDays(2), CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            macroOnlyMode: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1));
            Assert.That(result.TotalSells, Is.Zero, "CycleTopZone must not fire at 98.69 % proximity when threshold is 99.0 %");
        }
    }

    /// <summary>
    /// Validates the post-halving window gate (ADR-007): when the same
    /// CycleTopZone setup that normally fires DistributionSell at T0+1day
    /// is run with the gate active, the trigger is suppressed because
    /// T0 = 2025-01-01 is only ~8 months past the 2024-04-19 halving,
    /// outside the configured [12, 30] window.
    /// </summary>
    [Test]
    public async Task RunBacktestAsync_MacroOnly_HalvingWindowGate_BlocksZone_OutsideWindow()
    {
        // Setup mirrors RunBacktestAsync_MacroOnly_Sell_WhenCycleTopOvervalued, but with
        // halving gate enabled (default Min=12, Max=30). At T0 = 2025-01-01, last halving
        // was 2024-04-19 (~8 months ago) → below Min=12 → zone suppressed.
        SetupTimestampsWithPrices((T0, 10_000.0), (T0.AddDays(1), 10_000.0));
        SetupMicroStrategyUnavailable();
        Setup1hCandles(athHigh: 100_000, currentLow: 10_000); // −90 % drawdown → StrongBuy fires
        Setup1dCandlesCycleTopZone();

        var sutWithHalvingGate = new BacktestService(
            _mockTradeAdvisor.Object,
            _mockStockCandleRepository.Object,
            _mockBinanceKlineService.Object,
            _mockMarketRegimeService.Object,
            _mockRiskControlService.Object,
            Options.Create(new RiskControlOptions()),
            _mockTradingRepository.Object,
            _mockAtrLookupService.Object,
            _mockMarketContextService.Object,
            Options.Create(new VolumeVolatilityOptions()),
            _mockBearMarketFilterService.Object,
            Options.Create(new BearMarketFilterOptions
            {
                Enabled = false,
                CycleTopSellZoneEnabled = true,
                CycleTopSellZoneInterval = "1d",
                CycleTopSellZoneLookbackDays = 1460,
                CycleTopSellZoneMinDrawupPercent = 350.0,
                CycleTopSellZoneMinAthProximityPercent = 90.0,
                CycleTopSellZoneMinMonthsAfterHalving = 12,
                CycleTopSellZoneMaxMonthsAfterHalving = 30,
            }));

        var result = await sutWithHalvingGate.RunBacktestAsync(
            T0, T0.AddDays(2), CancellationToken.None,
            persist: false,
            momentumBrakeMaxRatio: DistributionTopThresholds.MomentumBrakeMaxRatio,
            troughMinDrawdownPercent: DistributionTopThresholds.IntermediateTroughMinDrawdownPercent,
            troughRecoveryProximityPercent: DistributionTopThresholds.IntermediateTroughRecoveryProximityPercent,
            confluenceThresholdSet: LongTimeframeConfluenceThresholdSet.Default,
            bearishRegimeMinLinearBullishScore: MarketRegimeThresholds.BearishRegimeMinLinearBullishScore,
            macroOnlyMode: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalBuys,  Is.EqualTo(1), "DeepCycleLow buy is unaffected by halving gate");
            Assert.That(result.TotalSells, Is.Zero,
                "T0 (~8 months post-halving) is outside [12, 30] window → DistributionSell suppressed");
        }
    }
}
