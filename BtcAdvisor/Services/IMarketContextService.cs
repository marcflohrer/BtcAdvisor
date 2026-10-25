namespace BtcAdvisor.Services;

/// <summary>
/// Provides volatility/volume context (Bollinger-band width, OBV slope, VWAP, MFI) at a point
/// in time for use as additive votes/penalties on the linear bullish/bearish score.
/// </summary>
public sealed record MarketContextSnapshot(
    double? BbWidthPercent,
    double? BbWidthMaPercent,
    double? ObvSlopePercent,
    double? VwapDistancePercent = null,
    bool VwapReclaimed = false,
    double? Mfi = null);

public interface IMarketContextService
{
    /// <summary>
    /// Returns Bollinger-band width and OBV-slope context at <paramref name="asOfUtc"/> for
    /// <paramref name="interval"/>. Any field is null when insufficient candles are available.
    /// </summary>
    Task<MarketContextSnapshot> GetSnapshotAsync(string interval, DateTime asOfUtc, CancellationToken cancellationToken);
}
