namespace BtcAdvisor.Repository.Models;

public class IndicatorDatumCurrentPrevious
{
    public Dictionary<string, List<IndicatorResult>> ? Current { get; set; }
    public Dictionary<string, List<IndicatorResult>> ? Previous { get; set; }

    public DateTime DateTime{ get; set; } = DateTime.UtcNow;
}
