using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Constants;
using BtcAdvisor.Repository.Models;
using static BtcAdvisor.Extensions.DateTimeExtensions;

namespace BtcAdvisor.Repository;

public class BitcoinRepository (ApplicationDbContext applicationDbContext) : IBitcoinRepository
{
    private readonly ApplicationDbContext _applicationDbContext = applicationDbContext;

    public async Task SaveIndicatorData (IndicatorDatum indicatorDatum, CancellationToken cancellationToken)
    {
        await _applicationDbContext.IndicatorData.AddAsync(indicatorDatum, cancellationToken);
        await _applicationDbContext.SaveChangesAsync(cancellationToken);
        var count = await _applicationDbContext.IndicatorData.CountAsync(cancellationToken: cancellationToken);
        Console.WriteLine($"{indicatorDatum.DateCreated} Currently Bitcoin {count} rows in database : adding {indicatorDatum.Indicator} :: indicator count {indicatorDatum.IndicatorResults.Count()} ::  {indicatorDatum.DateCreated} :: {indicatorDatum.PeriodMinutes}.");
    }

    public async Task<IndicatorDatumCurrentPrevious> GetIndicatorStatistics (DateTime dateTime, string timeSpan, string indicator, int? period, CancellationToken cancellationToken)
    {
        if (_applicationDbContext.IndicatorData is null)
        {
            throw new InvalidOperationException ("Database column is null");
        }
        if(!await _applicationDbContext.IndicatorData.AnyAsync(cancellationToken: cancellationToken))
        {
            throw new InvalidDataException("No data in database found!");
        }
        // Umrechnen des Zeitintervalls in Minuten
        if (!BitcoinConstants.MapPeriodToMinutes.TryGetValue(timeSpan, out var minutes))
        {
            throw new ArgumentException($"Unsupported time span: {timeSpan}");
        }

        dateTime = dateTime.ToUtcDateTime();

        var periodQuery = period == null ? "_" : $"_{period}_";
        // Abfrage aller IndicatorData inklusive der IndicatorResults, sortiert nach DateCreated absteigend
        var data = await _applicationDbContext.IndicatorData
            .Include(x => x.IndicatorResults)
            .Where(id => id.IndicatorResults
                .Any(ir => ir.IndicatorId.Contains($"_{timeSpan}_") && ir.IndicatorId.Contains($"_{indicator}{periodQuery}"))
                && id.IndicatorResults.Any(result => result.Indicator == indicator)
                && id.DateCreated <= dateTime)
            .OrderByDescending(x => x.DateCreated)
            .Take(1)
            .ToListAsync(cancellationToken: cancellationToken);

        // Annahme, dass die neuesten und die vorherigen Datensätze immer vorhanden sind
        var newest = data.Single();

        var previousData = await _applicationDbContext.IndicatorData
            .Include(x => x.IndicatorResults)
            .Where(id => id.IndicatorResults
                .Any(ir => ir.IndicatorId.Contains($"_{timeSpan}_") && ir.IndicatorId.Contains($"_{indicator}{periodQuery}"))
                && id.IndicatorResults.Any(result => result.Indicator == indicator)
                && id.DateCreated < newest.DateCreated)
            .OrderByDescending(x => x.DateCreated)
            .Take(1)
            .ToListAsync(cancellationToken: cancellationToken);

        var previous = previousData.SingleOrDefault() ?? newest;

        // Funktion zum Umwandeln von IndicatorData zu Dictionary
        static Dictionary<string, List<IndicatorResult>>? convertToDictionary(IndicatorDatum? indicatorData) => indicatorData?.IndicatorResults
                .GroupBy(r => r.IndicatorId)
                .ToDictionary(g => g.Key, g => g.ToList());

        // Erstellen des DTOs
        var result = new IndicatorDatumCurrentPrevious
        {
            Current = convertToDictionary(newest),
            Previous = convertToDictionary(previous),
            DateTime = newest.DateCreated,
        };

        return result;
    }

    public async Task<IReadOnlyCollection<DateTime>> GetDistinctTimestampsAsync(DateTime startDate, DateTime endDate, int? periodMinutes = null, CancellationToken cancellationToken = default)
    {
        var startUtc = startDate.ToUtcDateTime();
        var endUtc = endDate.ToUtcDateTime();

        var query = _applicationDbContext.IndicatorData
            .Where(i => i.DateCreated >= startUtc && i.DateCreated <= endUtc);

        if (periodMinutes.HasValue)
        {
            query = query.Where(i => i.PeriodMinutes == periodMinutes.Value);
        }

        var timestamps = await query
            .Select(i => i.DateCreated)
            .Distinct()
            .OrderBy(i => i)
            .ToListAsync(cancellationToken);

        return timestamps.AsReadOnly();
    }

    public Task<IReadOnlyCollection<DateTime>> GetDistinctTimestampsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        return GetDistinctTimestampsAsync(startDate, endDate, null, cancellationToken);
    }
}
