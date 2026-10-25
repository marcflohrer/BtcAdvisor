using BtcAdvisor.Repository;
using BtcAdvisor.Services.Models;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;

public class ShortSellerDetectorService(
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider dateTimeProvider,
    ILogger<ShortSellerDetectorService>? log = null) : IShortSellerDetectorService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider = dateTimeProvider;
    private readonly ILogger<ShortSellerDetectorService>? _log = log;
    public string DebugString { get; private set; } = string.Empty;


    public async Task<ShortSellerPhase> GetShortSellerPhase(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<IStockCandleRepository>();
        var btcStats  = scope.ServiceProvider.GetRequiredService<IBitcoinStatisticsService>();
        var stateRepo = scope.ServiceProvider.GetRequiredService<IShortSellerStateRepository>();

        var dbg = new System.Text.StringBuilder();
        ShortSellerPhase Done(ShortSellerPhase p)
        {
            dbg.AppendLine($"=> return Phase = {p}");
            DebugString = dbg.ToString();
            return p;
        }
        var nowUtc = _dateTimeProvider.GetUtcNow();
        var window = TimeSpan.FromMinutes(60);
        var t0 = nowUtc;
        var t1 = nowUtc.Add(-window);

        dbg.AppendLine($"nowUtc={nowUtc:o}");
        dbg.AppendLine($"window=60m  t0=[{t0:o})  t1=[{t1:o})");
        dbg.AppendLine();

        // Fensterstarts korrekt setzen:
        var startRecent = nowUtc - window;        // [now-60m, now)
        var startBefore = nowUtc - window - window; // [now-120m, now-60m)

        var mstrRecent = await stockRepo.GetStockCandle("MSTR", startRecent, window, ct);
        var mstrBefore = await stockRepo.GetStockCandle("MSTR", startBefore, window, ct);

        if (mstrRecent is null || mstrBefore is null)
        {
            dbg.AppendLine("MSTR: fehlende Candles -> None");
            return Done(ShortSellerPhase.None);
        }

        var mstrRecentMid = (double)((mstrRecent.HighPrice + mstrRecent.LowPrice) / 2m);
        var mstrBeforeMid = (double)((mstrBefore.HighPrice + mstrBefore.LowPrice) / 2m);
        var mstrUp = mstrRecentMid > mstrBeforeMid;
        var mstrDown = mstrRecentMid < mstrBeforeMid;

        dbg.AppendLine($"MSTR recent:  ts={mstrRecent.Timestamp:o}  hi={mstrRecent.HighPrice} lo={mstrRecent.LowPrice}  mid={mstrRecentMid:F4}");
        dbg.AppendLine($"MSTR before:  ts={mstrBefore.Timestamp:o}  hi={mstrBefore.HighPrice} lo={mstrBefore.LowPrice}  mid={mstrBeforeMid:F4}");
        dbg.AppendLine($"MSTR dir: up={mstrUp} down={mstrDown}  rel={(mstrRecentMid - mstrBeforeMid) / (mstrBeforeMid==0?1:mstrBeforeMid):P4}");
        dbg.AppendLine();

        var highPricePrevious = (double)mstrBefore.HighPrice;
        var lowPricePrevious = (double)mstrBefore.LowPrice;
        var highPriceNow  = (double)mstrRecent.HighPrice;
        var lowPriceNow  = (double)mstrRecent.LowPrice;

        var lowerHigh = highPriceNow < highPricePrevious;
        var higherLow = lowPriceNow > lowPricePrevious;

        // Break-Metriken (negativ für Down, positiv für Up)
        var downBreak = (lowPriceNow / highPricePrevious) - 1.0;  // z.B. -0.044 = -4.4%
        var upBreak   = (highPriceNow / lowPricePrevious) - 1.0;  // z.B. +0.025 = +2.5%

        // BTC 60m (rollierendes Look-back)
        var btcRecent = await btcStats.GetStatistics(nowUtc,          true, "1h", ct);
        var btcBefore = await btcStats.GetStatistics(nowUtc - window, true, "1h", ct);
        if (btcRecent is null || btcBefore is null || btcBefore.Price == 0)
        {
            dbg.AppendLine("BTC: fehlende Stats -> None");
            return Done(ShortSellerPhase.None);
        }

        // Relativänderungen (gegen vorheriges 60m-Fenster)
        var relativeMstrChange = (mstrRecentMid - mstrBeforeMid) / mstrBeforeMid;
        var relativeBtcChange  = (btcRecent.Price - btcBefore.Price) / btcBefore.Price;
        var differenceBetweenRelativeChanges    = relativeMstrChange - relativeBtcChange;

        // Schwellen (feinfühlig für deinen Case)
        const double BreakPct   = 0.045;  // 4.5%
        const double BtcGuard   = 0.011;  // 1.1%  
        const double Underperf  = 0.010;  // 1.0 %
        const double MinBtcMove = 0.001;  // 0.1%

        // 0) Gleichlauf ohne Breaks -> Unchanged (früh aussteigen)
        var sameSign =
            (relativeMstrChange == 0 && relativeBtcChange == 0) || // weder btc noch mstr haben sich verändert
            (relativeMstrChange > 0 && relativeBtcChange > 0) || // relative Veränderung von btc und mstr nach oben
            (relativeMstrChange < 0 && relativeBtcChange < 0); // relative Veränderung von btc und mstr nach unten

        var anyBreak = (downBreak <= -BreakPct) || (upBreak >= BreakPct); // hat mstr eine Schwankung außerhalb der Schwellwerte?

        // *** FRÜHRÜCKGABE für ruhigen Gleichlauf ***
        if (sameSign && !anyBreak)
        {
            dbg.AppendLine("Decision: Gleichlauf & keine Range-Breaks -> Unchanged");
            return Done(ShortSellerPhase.Unchanged);
        }

        dbg.AppendLine($"MSTR extremes: prev[H={highPricePrevious:F4},L={lowPricePrevious:F4}]  now[H={highPriceNow:F4},L={lowPriceNow:F4}]");
        dbg.AppendLine($"flags: lowerHigh={lowerHigh}, higherLow={higherLow}");
        dbg.AppendLine($"breaks: downBreak={downBreak:P4}, upBreak={upBreak:P4}  (BreakPct={BreakPct:P2})");
        dbg.AppendLine($"BTC 60m: recent={btcRecent.Price:F4} before={btcBefore.Price:F4}  relBtc={relativeBtcChange:P4} (guard±{BtcGuard:P2})");
        dbg.AppendLine($"relMstr={relativeMstrChange:P4}  diff(relMstr-relBtc)={differenceBetweenRelativeChanges:P4}  (Underperf={Underperf:P2}, MinBtcMove={MinBtcMove:P2})");
        dbg.AppendLine();

        var previous = await stateRepo.GetLatestShortSellerPhase(ct);

        // 1) Primärregel: Extrem-Breaks
        if (lowerHigh && downBreak <= -BreakPct && relativeBtcChange > -BtcGuard)
        {
            dbg.AppendLine("Decision: LowerHigh & starker DownBreak bei ruhigem BTC -> IsSelling");
            return Done(previous == ShortSellerPhase.IsSelling ? ShortSellerPhase.Unchanged : ShortSellerPhase.IsSelling);
        }
        if (higherLow && upBreak >= BreakPct && relativeBtcChange < BtcGuard)
        {
            dbg.AppendLine("Decision: HigherLow & starker UpBreak bei ruhigem BTC -> IsBuying");
            return Done(previous == ShortSellerPhase.IsBuying ? ShortSellerPhase.Unchanged : ShortSellerPhase.IsBuying);
        }

        // 2) Fallback: relative Under/Outperformance
        if (Math.Abs(relativeBtcChange) >= MinBtcMove)
        {
            if (differenceBetweenRelativeChanges <= -Underperf)
            {
                dbg.AppendLine("Decision: MSTR underperformt BTC deutlich -> IsSelling");
                return Done(previous == ShortSellerPhase.IsSelling ? ShortSellerPhase.Unchanged : ShortSellerPhase.IsSelling);
            }
            if (differenceBetweenRelativeChanges >= Underperf)
            {
                dbg.AppendLine("Decision: MSTR outperformt BTC deutlich -> IsBuying");
                return Done(previous == ShortSellerPhase.IsBuying ? ShortSellerPhase.Unchanged : ShortSellerPhase.IsBuying);
            }
        }

        // 3) Gleichlauf → Unchanged
        if ((relativeMstrChange >= 0 && relativeBtcChange >= 0) || (relativeMstrChange <= 0 && relativeBtcChange <= 0))
        {
            dbg.AppendLine("Decision: Gleichlauf ohne klares Signal -> Unchanged");
            return Done(ShortSellerPhase.Unchanged);
        }

        // 4) Kein klares Signal
        dbg.AppendLine("Decision: keine Aussage -> None");
        return Done(ShortSellerPhase.None);

    }

    public async Task DetectAndPersistAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var stateRepo = scope.ServiceProvider.GetRequiredService<IShortSellerStateRepository>();

        var phase = await GetShortSellerPhase(ct);
        if (phase is ShortSellerPhase.None or ShortSellerPhase.Unchanged)
        {
            _log?.LogDebug("Detector: Phase={Phase} → nichts zu speichern.", phase);
            return;
        }

        var last = await stateRepo.GetLatestShortSellerPhase(ct);
        if (last == phase)
        {
            _log?.LogDebug("Detector: Phase unverändert ({Phase}) → kein Save.", phase);
            return;
        }

        var nowUtc = _dateTimeProvider.GetUtcNow();
        await stateRepo.SaveAsync(phase, nowUtc, ct);
        _log?.LogInformation("Detector: Neuer ShortSellerState gespeichert: {Phase} @ {Utc}", phase, nowUtc);
    }
}
