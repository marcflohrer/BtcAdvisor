namespace BtcAdvisor.Repository.Models;

public class BuyActionLog
{
    public int Id { get; set;}

    public string TradeAdvice { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public DateTime TradingDate { get; set; }

    public bool IsUrgent { get; set; }

    public bool IsLongTimeframe { get; set; }

    public bool IsValidated { get; set; }

    public int? AlignedRanges { get; set; }

    public int? TrendCountSum { get; set; }

    public virtual ICollection<SellActionLog>? SellActionLogs { get; set; }
}