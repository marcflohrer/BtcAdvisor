namespace BtcAdvisor.Models;

public record ApiSettings
{
    public string MarketDataAppApiKey { get; set; } = string.Empty;
}
