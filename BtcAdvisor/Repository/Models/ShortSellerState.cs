using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Repository.Models;

public class ShortSellerState
{
    public int Id { get; set; }
    public ShortSellerPhase Phase { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
