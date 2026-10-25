using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class BuyActionLogRepositoryTests
{
    private DbContextOptions<ApplicationDbContext> _options;
    private ApplicationDbContext _context;
    private BuyActionLogRepository _repository;
    private Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
    private Mock<IServiceProvider> _serviceProviderMock;

    [SetUp]
    public void SetUp()
    {
        // Nur In-Memory-Datenbank für den Test
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Neuen Kontext mit den Test-Options erstellen
        _context = new ApplicationDbContext(_options);

        _serviceProviderMock = new Mock<IServiceProvider>();
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ApplicationDbContext)))
            .Returns(_context);

        _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();
        _serviceScopeFactoryMock
            .Setup(s => s.CreateScope().ServiceProvider)
            .Returns(_serviceProviderMock.Object);

        // Repository mit dem In-Memory-Kontext initialisieren
        _repository = new BuyActionLogRepository(_serviceScopeFactoryMock.Object);

        // Testdaten einfügen
        SeedTestData();
    }

    private void SeedTestData()
    {
        // IndicatorData mit allen Pflichtfeldern einfügen
        _context.IndicatorData.Add(new IndicatorDatum
        {
            ApiId = "1", // Beispielwert
            Indicator = "TestIndicator" // Beispielwert
        });
        _context.SaveChanges();

        // BuyActionLogs und SellActionLogs einfügen
        var buyLogs = new[]
        {
            new BuyActionLog { TradingDate = new DateTime(2023, 1, 1), Price = 100, IsValidated = true },
            new BuyActionLog { TradingDate = new DateTime(2023, 1, 2), Price = 90, IsValidated = true },
            new BuyActionLog { TradingDate = new DateTime(2023, 1, 3), Price = 80, IsValidated = true },
            new BuyActionLog { TradingDate = new DateTime(2023, 1, 4), Price = 70, IsValidated = true },
            new BuyActionLog { TradingDate = new DateTime(2023, 1, 5), Price = 75, TradeAdvice = "PanicBuy", IsValidated = true },
        };
        _context.BuyActionLogs.AddRange(buyLogs);

        var sellLogs = new[]
        {
            new SellActionLog { TradingDate = new DateTime(2023, 1, 1), TradeAdvice = "NormalSell" },
            new SellActionLog { TradingDate = new DateTime(2023, 1, 2), TradeAdvice = "FireSell" },
        };
        _context.SellActionLogs.AddRange(sellLogs);

        _context.SaveChanges();
    }

    [Test]
    public async Task GetBuyActionLogs_ReturnsFilteredLogs()
    {
        // Arrange
        var start = new DateTime(2023, 1, 1);
        var end = new DateTime(2023, 1, 2);

        // Act
        var result = await _repository.GetBuyActionLogs(start, end, null, CancellationToken.None);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Has.All.Matches<BuyActionLog>(log => log.TradingDate >= start && log.TradingDate <= end));
    }

    [Test]
    public async Task GetBuyActionLogs_ReturnsEmptyIfNoData()
    {
        // Arrange: Leere Datenbank für diesen Test
        var emptyOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var emptyContext = new ApplicationDbContext(emptyOptions);
        emptyContext.IndicatorData.Add(new IndicatorDatum
        {
            ApiId = "1",
            Indicator = "TestIndicator"
        });
        emptyContext.SaveChanges();
        _serviceScopeFactoryMock
            .Setup(s => s.CreateScope().ServiceProvider.GetService(typeof(ApplicationDbContext)))
            .Returns(emptyContext);

        var emptyRepo = new BuyActionLogRepository(_serviceScopeFactoryMock.Object);

        // Act
        var result = await emptyRepo.GetBuyActionLogs(DateTime.Now, DateTime.Now, null, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetLowestBuyPriceSinceLatestFireSell_ReturnsLowestPrice()
    {
        // Act
        var result = await _repository.GetLowestBuyPriceSinceLatestFireSell(DateTime.Now, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Price, Is.EqualTo(70));
    }

    [Test]
    public async Task GetHighestPanicBuyPriceSinceLowestBuyAfterFireSell_ReturnsLowestPrice()
    {
        // Act
        var result = await _repository.GetHighestPanicBuyPriceAfterLowestBuyPriceSinceLatestFireSell(DateTime.Now, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Price, Is.EqualTo(75));
    }

    [Test]
    public async Task GetLowestBuyPriceSinceLatestFireSell_ReturnsNullIfNoFireSell()
    {
        // Arrange: Kein FireSell in den Testdaten
        _context.SellActionLogs.RemoveRange(_context.SellActionLogs);
        _context.SellActionLogs.Add(new SellActionLog { Id = 3, TradingDate = new DateTime(2023, 1, 1), TradeAdvice = "NormalSell" });
        _context.SaveChanges();

        // Act
        var result = await _repository.GetLowestBuyPriceSinceLatestFireSell(DateTime.Now, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Null);
    }

    /// <summary>
    /// Regression: GetHighestPanicBuyPriceAfterLowestBuyPriceSinceLatestFireSell previously used
    /// == "PanicBuy" which excluded variants like "PanicBuy2k", "PanicBuyStrict", etc.
    /// Fix: .Contains("PanicBuy") is now used so all PanicBuy variants are included.
    /// </summary>
    [Test]
    public async Task GetHighestPanicBuyPriceSinceLowestBuyAfterFireSell_IncludesPanicBuyVariants()
    {
        // Arrange: add PanicBuy2k and PanicBuyStrict entries after the FireSell
        _context.BuyActionLogs.Add(new BuyActionLog { TradingDate = new DateTime(2023, 1, 6), Price = 80, TradeAdvice = "PanicBuy2k" });
        _context.BuyActionLogs.Add(new BuyActionLog { TradingDate = new DateTime(2023, 1, 7), Price = 85, TradeAdvice = "PanicBuyStrict" });
        _context.SaveChanges();

        // Act
        var result = await _repository.GetHighestPanicBuyPriceAfterLowestBuyPriceSinceLatestFireSell(DateTime.Now, CancellationToken.None);

        // Assert: PanicBuyStrict at 85 is the highest PanicBuy variant
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Price, Is.EqualTo(85));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }
}
