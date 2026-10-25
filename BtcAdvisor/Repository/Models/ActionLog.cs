using System.ComponentModel.DataAnnotations;

namespace BtcAdvisor.Services.Models;

public class ActionLog
{
    public long Id { get; set; }
    public string? Type { get; set; } // "Buy" oder "Sell"
    public decimal Price { get; set; }
    public string? TradeAdvice { get; set; }
    public string? DebugString { get; set; }
    public DateTime TradingDate { get; set; }
    public bool IsLongTimeframe { get; set; }
}