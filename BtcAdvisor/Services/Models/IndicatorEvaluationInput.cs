namespace BtcAdvisor.Repository.Models;

public class IndicatorEvaluationInput
{
    public Dictionary<string, IndicatorInputDatum> Current { get; set; } = [];
    public Dictionary<string, IndicatorInputDatum> Previous { get; set; } = [];
    public double Price { get; set; }
    public int Period { get; set; }

    public DateTime DateTime { get; set; } = DateTime.UtcNow;
}
