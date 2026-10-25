using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Tests;

/// <summary>
/// Integrationstests für <see cref="BitcoinRepository.GetDistinctTimestampsAsync"/>.
/// Die Methode verwendet raw SQL (date_trunc), das die EF InMemory-Provider nicht
/// unterstützen. Die Tests werden daher übersprungen, sofern keine echte PostgreSQL-
/// Datenbankverbindung vorhanden ist, und dienen primär als Dokumentation der
/// erwarteten Semantik.
/// </summary>
[TestFixture]
[Ignore("GetDistinctTimestampsAsync verwendet raw SQL (date_trunc) – requires real PostgreSQL")]
public class BitcoinRepositoryBacktestTests
{
    private DbContextOptions<ApplicationDbContext> _options;
    private ApplicationDbContext _context;
    private BitcoinRepository _repository;

    [SetUp]
    public void SetUp()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(_options);
        _repository = new BitcoinRepository(_context);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddIndicatorDatum(DateTime dateCreated)
    {
        _context.IndicatorData.Add(new IndicatorDatum
        {
            ApiId = Guid.NewGuid().ToString(),
            Indicator = "RSI",
            DateCreated = dateCreated
        });
    }

    private void SaveChanges() => _context.SaveChanges();

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetDistinctTimestampsAsync_WhenNoData_ReturnsEmptyList()
    {
        // Arrange
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = await _repository.GetDistinctTimestampsAsync(start, end, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetDistinctTimestampsAsync_ReturnsOnlyTimestampsInRange()
    {
        // Arrange
        var start = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        AddIndicatorDatum(new DateTime(2025, 1, 1, 9, 59, 0, DateTimeKind.Utc));  // vor Range
        AddIndicatorDatum(new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc)); // Grenze – innen
        AddIndicatorDatum(new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc)); // innen
        AddIndicatorDatum(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)); // Grenze – innen
        AddIndicatorDatum(new DateTime(2025, 1, 1, 12, 1, 0, DateTimeKind.Utc)); // nach Range
        SaveChanges();

        // Act
        var result = await _repository.GetDistinctTimestampsAsync(start, end, CancellationToken.None);

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task GetDistinctTimestampsAsync_MultipleEntriesPerMinute_ReturnsOneTimestampPerMinute()
    {
        // Arrange – drei Einträge in der gleichen Minute
        var base_ = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        AddIndicatorDatum(base_.AddSeconds(0));
        AddIndicatorDatum(base_.AddSeconds(15));
        AddIndicatorDatum(base_.AddSeconds(45));
        SaveChanges();

        // Act
        var result = await _repository.GetDistinctTimestampsAsync(base_, base_.AddHours(1), CancellationToken.None);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetDistinctTimestampsAsync_ReturnsTimestampsInAscendingOrder()
    {
        // Arrange
        var start = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        AddIndicatorDatum(new DateTime(2025, 1, 1, 10, 2, 0, DateTimeKind.Utc));
        AddIndicatorDatum(new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        AddIndicatorDatum(new DateTime(2025, 1, 1, 10, 1, 0, DateTimeKind.Utc));
        SaveChanges();

        // Act
        var result = await _repository.GetDistinctTimestampsAsync(start, start.AddHours(1), CancellationToken.None);
        var list = result.ToList();

        // Assert
        Assert.That(list, Is.Ordered.Ascending);
    }

    [Test]
    public async Task GetDistinctTimestampsAsync_DifferentMinutes_EachReturnedOnce()
    {
        // Arrange – 3 verschiedene Minuten, je 2 Einträge
        var start = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        for (var m = 0; m < 3; m++)
        {
            AddIndicatorDatum(start.AddMinutes(m).AddSeconds(10));
            AddIndicatorDatum(start.AddMinutes(m).AddSeconds(50));
        }
        SaveChanges();

        // Act
        var result = await _repository.GetDistinctTimestampsAsync(start, start.AddHours(1), CancellationToken.None);

        // Assert – genau 3 Timestamps (einer pro Minute)
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task GetDistinctTimestampsAsync_MultipleMinutes_ReturnsMaxTimestampPerMinute()
    {
        // Arrange – zwei Einträge in Minute 10:00, wir erwarten den späteren Sekundenwert
        var base_ = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var early = base_.AddSeconds(5);
        var late = base_.AddSeconds(55);
        AddIndicatorDatum(early);
        AddIndicatorDatum(late);
        SaveChanges();

        // Act
        var result = await _repository.GetDistinctTimestampsAsync(base_, base_.AddHours(1), CancellationToken.None);
        var single = result.Single();

        // Assert – Max wird zurückgegeben
        Assert.That(single, Is.EqualTo(late));
    }
}
