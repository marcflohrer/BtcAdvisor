using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Repository;

public class ShortSellerStateRepository (ApplicationDbContext applicationDbContext) : IShortSellerStateRepository
{
    private readonly ApplicationDbContext _applicationDbContext = applicationDbContext;

    public async Task SaveAsync (ShortSellerPhase phase, DateTime dateTime, CancellationToken cancellationToken)
    {
        var utc = dateTime.Kind == DateTimeKind.Utc ? dateTime : dateTime.ToUniversalTime ();
        await _applicationDbContext.ShortSellerStates.AddAsync (new ShortSellerState
        {
            Phase = phase,
                CreatedAtUtc = new DateTimeOffset (utc)
        }, cancellationToken);
        var count = await _applicationDbContext.SaveChangesAsync (cancellationToken);
        Console.WriteLine ($"{utc} Currently {count} short seller phases in database");
    }

    public async Task<ShortSellerPhase> GetLatestShortSellerPhase (CancellationToken cancellationToken)
    {
        if (_applicationDbContext.ShortSellerStates is null)
        {
            throw new InvalidOperationException ("Database column ShortSellerPhase is null");
        }
        if (!await _applicationDbContext.ShortSellerStates.AnyAsync (cancellationToken: cancellationToken))
        {
            return ShortSellerPhase.None;
        }

        // Abfrage aller IndicatorData inklusive der IndicatorResults, sortiert nach DateCreated absteigend
        var shortSellerState = await _applicationDbContext.ShortSellerStates
            .OrderByDescending (x => x.CreatedAtUtc)
            .Take (1)
            .SingleAsync (cancellationToken: cancellationToken);

        return shortSellerState.Phase;
    }

    public async Task<List<(string TradeAdvice, decimal Price, DateTime TimeStamp)>> GetHistoryAsync (int take,
        CancellationToken ct)
    {
        var buyLogs = await _applicationDbContext.BuyActionLogs
            .OrderByDescending (x => x.TradingDate)
            .Take (take)
            .Select (x => new { x.TradeAdvice, x.Price, x.TradingDate })
            .ToListAsync (ct);

        var sellLogs = await _applicationDbContext.SellActionLogs
            .OrderByDescending (x => x.TradingDate)
            .Take (take)
            .Select (x => new { x.TradeAdvice, x.Price, x.TradingDate })
            .ToListAsync (ct);

        var tradeLogs = buyLogs.Union (sellLogs);

        // Optional wieder aufsteigend für Timeline-Ansicht
        return [..tradeLogs
            .OrderByDescending (x => x.TradingDate)
            .Select (x => (x.TradeAdvice, x.Price, x.TradingDate))
        ];
    }
}
