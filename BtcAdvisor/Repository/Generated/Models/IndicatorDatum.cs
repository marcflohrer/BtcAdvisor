namespace BtcAdvisor.Repository.Models;

public partial class IndicatorDatum
{
    public int Id { get; set; }

    public string ApiId { get; set; } = null!;

    public int PeriodMinutes { get; set; }

    public string Indicator { get; set; } = null!;

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;

    public virtual ICollection<IndicatorError> IndicatorErrors { get; set; } = [];

    public virtual ICollection<IndicatorResult> IndicatorResults { get; set; } = [];
}
