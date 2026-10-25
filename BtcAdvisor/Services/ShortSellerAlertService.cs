using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;
public class ShortSellerAlertService(
    ApplicationDbContext db,
    ITelegramBotService telegram,
    ILogger<ShortSellerAlertService> log) : IShortSellerAlertService
{
    private readonly ApplicationDbContext _db = db;
    private readonly ITelegramBotService _telegram = telegram;
    private readonly ILogger<ShortSellerAlertService> _log = log;

    public async Task NotifyIfPhaseChangedAsync (CancellationToken ct = default)
    {
        // Die zwei neuesten States laden
        var lastTwo = await _db.ShortSellerStates
            .OrderByDescending (s => s.CreatedAtUtc)
            .Take (2)
            .ToListAsync (ct);

        if (lastTwo.Count < 2)
        {
            return;
        }

        var current = lastTwo[0];
        var previous = lastTwo[1];

        // Kein Wechsel → nichts zu tun
        if (current.Phase == previous.Phase)
        {
            return;
        }

        // Bereits für diesen State benachrichtigt?
        var alreadySent = await _db.ShortSellerPhaseNotifications
            .AnyAsync (n => n.ShortSellerStateId == current.Id, ct);

        if (alreadySent)
        {
            return;
        }

        // Kompakter, nützlicher Log-Eintrag
        _log.LogInformation ("ShortSellerPhase change: {Prev} → {Curr} @ {AtUtc}",
            previous.Phase, current.Phase, current.CreatedAtUtc);

        var msg =
            $"⚠️ Short-Seller-Status geändert: {previous.Phase} → {current.Phase}\n" +
            $"UTC: {current.CreatedAtUtc:yyyy-MM-dd HH:mm:ss}";

        await _telegram.SendMessageAsync (msg);

        // Idempotent festhalten (UNIQUE auf ShortSellerStateId schützt doppelt)
        _db.ShortSellerPhaseNotifications.Add (new ShortSellerPhaseNotification
        {
            ShortSellerStateId = current.Id
        });

        try
        {
            await _db.SaveChangesAsync (ct);
        }
        catch (DbUpdateException ex)
        {
            // Falls parallel bereits eingetragen → kein Problem, Nachricht ging schon raus.
            _log.LogDebug (ex, "ShortSellerPhaseNotifications UNIQUE constraint hit (parallel insert). Ignored.");
        }
    }
}
