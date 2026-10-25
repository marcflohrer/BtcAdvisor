namespace BtcAdvisor.Repository.Models;

public class SellActionLog
{
    public int Id { get; set;}

    public string TradeAdvice { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int? BuyActionLogId { get; set; }

    public bool IsUrgent { get; set; }

    public bool IsLongTimeframe { get; set; }

    public bool IsValidated { get; set; }

    public int? AlignedRanges { get; set; }

    public int? TrendCountSum { get; set; }

    public BuyActionLog? BuyActionLog { get; set; } = new BuyActionLog();

    public DateTime TradingDate { get; set; } = new DateTime(1900,1,1);
}