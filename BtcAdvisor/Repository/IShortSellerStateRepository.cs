using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Repository;

public interface IShortSellerStateRepository
{

    public Task SaveAsync (ShortSellerPhase shortSellerPhase, DateTime dateTime, CancellationToken cancellationToken);

    public Task<ShortSellerPhase> GetLatestShortSellerPhase (CancellationToken cancellationToken);

    public Task < List < (string TradeAdvice, decimal Price, DateTime TimeStamp) >> GetHistoryAsync (int take,
        CancellationToken ct);
}
