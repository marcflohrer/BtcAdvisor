namespace BtcAdvisor.Repository.Models;

public partial class IndicatorResult
{
    public int Id { get; set; }

    public int IndicatorDataId { get; set; }

    public string IndicatorId { get; set; } = null!;

    public string Indicator { get; set; } = null!;

    public double? Conversion { get; set; }

    public double? Base { get; set; }

    public double? SpanA { get; set; }

    public double? SpanB { get; set; }

    public double? CurrentSpanA { get; set; }

    public double? CurrentSpanB { get; set; }

    public double? LaggingSpanA { get; set; }

    public double? LaggingSpanB { get; set; }

    public double? ValueMacd { get; set; }

    public double? ValueMacdsignal { get; set; }

    public double? ValueMacdhist { get; set; }

    public double? ValueK { get; set; }

    public double? ValueD { get; set; }

    public double? Value { get; set; }
    public double? ValueFastK { get; set; }
    public double? ValueFastD { get; set; }

    public virtual IndicatorDatum IndicatorData { get; set; } = null!;
}
