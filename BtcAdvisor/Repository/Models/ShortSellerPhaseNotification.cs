namespace BtcAdvisor.Repository.Models;

public class ShortSellerPhaseNotification
{
    public int Id { get; set; }

    /// <summary>
    /// Verweist auf den ShortSellerState, für den eine Benachrichtigung gesendet wurde.
    /// UNIQUE – pro State genau eine Notification.
    /// </summary>
    public int ShortSellerStateId { get; set; }

    public DateTimeOffset SentAtUtc { get; set; } = DateTimeOffset.UtcNow;

    // Optional: Navigation
    public ShortSellerState ShortSellerState { get; set; } = default!;
}
