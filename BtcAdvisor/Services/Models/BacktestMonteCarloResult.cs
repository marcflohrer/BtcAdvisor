namespace BtcAdvisor.Services.Models;

/// <summary>
/// Ergebnis einer Monte-Carlo-Signifikanzanalyse: testet, ob die realen
/// Backtest-Deals statistisch signifikant besser sind als zufaellig gewaehlte
/// Trades mit identischer Haltedauer im selben Zeitraum.
/// </summary>
public class BacktestMonteCarloResult
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int Iterations { get; set; }
    public int ValidIterations { get; set; }
    public int ActualTradeCount { get; set; }
    public double ActualTotalReturnPercent { get; set; }
    public double ActualCompoundedReturnPercent { get; set; }
    public double ActualWinRatePercent { get; set; }
    public double ActualMeanReturnPercent { get; set; }

    /// <summary>Haltedauer der realen Deals (Stunden), die fuer die zufaelligen Simulationen wiederverwendet wurden.</summary>
    public List<double> ActualHoldingHours { get; set; } = new();

    public NullDistributionStats TotalReturn { get; set; } = new();
    public NullDistributionStats CompoundedReturn { get; set; } = new();
    public NullDistributionStats WinRate { get; set; } = new();
    public NullDistributionStats MeanReturn { get; set; } = new();

    /// <summary>Klartext-Interpretation auf Basis der p-Werte.</summary>
    public string Verdict { get; set; } = string.Empty;
}

/// <summary>
/// Verteilungsstatistik einer einzelnen Metrik ueber alle Monte-Carlo-Iterationen.
/// </summary>
public class NullDistributionStats
{
    public double Mean { get; set; }
    public double Median { get; set; }
    public double StdDev { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double P05 { get; set; }
    public double P25 { get; set; }
    public double P75 { get; set; }
    public double P95 { get; set; }

    /// <summary>
    /// Anteil der zufaelligen Iterationen, deren Metrik &gt;= dem tatsaechlichen Wert ist.
    /// p &lt; 0.05 = signifikant besser als Zufall, p &gt; 0.5 = Zufallstreffer.
    /// </summary>
    public double PValueGreaterOrEqual { get; set; }

    /// <summary>Histogramm-Buckets der Null-Verteilung.</summary>
    public List<HistogramBucket> Histogram { get; set; } = new();
}

public record HistogramBucket(double BucketStart, double BucketEnd, int Count);
