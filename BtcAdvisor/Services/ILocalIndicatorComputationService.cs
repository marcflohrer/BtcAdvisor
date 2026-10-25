namespace BtcAdvisor.Services;

public sealed record MacdPoint(double? Macd, double? Signal, double? Histogram);

public sealed record StochasticPoint(double? K, double? D);

public sealed record IchimokuPoint(double? TenkanSen, double? KijunSen, double? SenkouSpanA, double? SenkouSpanB, double? ChikouSpan);

public sealed record BollingerPoint(double? Upper, double? Middle, double? Lower, double? WidthPercent);

public interface ILocalIndicatorComputationService
{
    IReadOnlyList<double?> ComputeSma(IReadOnlyList<double> closes, int period);

    IReadOnlyList<double?> ComputeEma(IReadOnlyList<double> closes, int period);

    IReadOnlyList<double?> ComputeRsi(IReadOnlyList<double> closes, int period);

    IReadOnlyList<double?> ComputeHma(IReadOnlyList<double> closes, int period);

    IReadOnlyList<IchimokuPoint> ComputeIchimoku(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int tenkanPeriod = 9,
        int kijunPeriod = 26,
        int senkouBPeriod = 52,
        int displacement = 26);

    IReadOnlyList<StochasticPoint> ComputeStoch(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int kPeriod = 14,
        int dPeriod = 3);

    IReadOnlyList<double?> ComputeCci(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int period = 20);

    IReadOnlyList<double?> ComputeAdx(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int period = 14);

    /// <summary>
    /// Computes Average True Range (Wilder smoothing) over the given OHLC series.
    /// Returns one value per input bar, with the first <paramref name="period"/> entries null
    /// (no warm-up data available). The first non-null value is the simple mean of the
    /// initial <paramref name="period"/> true ranges; subsequent values use Wilder's
    /// recursive smoothing: ATRᵢ = (ATRᵢ₋₁·(period-1) + TRᵢ) / period.
    /// </summary>
    IReadOnlyList<double?> ComputeAtr(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int period = 14);

    /// <summary>
    /// Computes Bollinger Bands over the given closes. WidthPercent = (Upper-Lower)/Middle*100.
    /// First (period-1) entries return null Upper/Middle/Lower/WidthPercent (insufficient warm-up).
    /// </summary>
    IReadOnlyList<BollingerPoint> ComputeBollinger(
        IReadOnlyList<double> closes,
        int period = 20,
        double stdDevMultiplier = 2.0);

    /// <summary>
    /// Computes On-Balance Volume cumulative series. Convention:
    /// <list type="bullet">
    ///   <item><description>OBV[0] = 0</description></item>
    ///   <item><description>OBV[i] = OBV[i-1] + volume[i] when close[i] &gt; close[i-1]</description></item>
    ///   <item><description>OBV[i] = OBV[i-1] - volume[i] when close[i] &lt; close[i-1]</description></item>
    ///   <item><description>OBV[i] = OBV[i-1] when close[i] == close[i-1]</description></item>
    /// </list>
    /// </summary>
    IReadOnlyList<double> ComputeObv(IReadOnlyList<double> closes, IReadOnlyList<double> volumes);

    /// <summary>
    /// Computes a rolling VWAP over the given window. Each output is the volume-weighted
    /// average of the typical price (high+low+close)/3 over the trailing <paramref name="period"/> bars.
    /// First (period-1) entries return null (insufficient warm-up).
    /// </summary>
    IReadOnlyList<double?> ComputeVwap(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        IReadOnlyList<double> volumes,
        int period = 20);

    /// <summary>
    /// Computes the Money Flow Index (0..100) over the given series.
    /// First <paramref name="period"/> entries return null (insufficient warm-up).
    /// </summary>
    IReadOnlyList<double?> ComputeMfi(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        IReadOnlyList<double> volumes,
        int period = 14);

    IReadOnlyList<double?> ComputeAo(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        int shortPeriod = 5,
        int longPeriod = 34);

    IReadOnlyList<double?> ComputeMom(IReadOnlyList<double> closes, int period = 10);

    IReadOnlyList<MacdPoint> ComputeMacd(
        IReadOnlyList<double> closes,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9);

    IReadOnlyList<StochasticPoint> ComputeStochf(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int kPeriod = 14,
        int dPeriod = 3);

    IReadOnlyList<double?> ComputeWillR(
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        int period = 14);

    IReadOnlyList<int> ComputeKickingByLength(
        IReadOnlyList<double> opens,
        IReadOnlyList<double> highs,
        IReadOnlyList<double> lows,
        IReadOnlyList<double> closes,
        double minBodyToRangeRatio = 0.8d);
}
