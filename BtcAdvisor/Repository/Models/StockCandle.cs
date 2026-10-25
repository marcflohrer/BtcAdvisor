using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BtcAdvisor.Repository.Models;

public class StockCandle
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int CandleID { get; set; }

    [Required]
    [StringLength(10)]
    public string Symbol { get; set; } = string.Empty;

    [Column(TypeName = "decimal(20, 8)")]
    public decimal OpenPrice { get; set; }

    [Column(TypeName = "decimal(20, 8)")]
    public decimal HighPrice { get; set; }

    [Column(TypeName = "decimal(20, 8)")]
    public decimal LowPrice { get; set; }

    [Column(TypeName = "decimal(20, 8)")]
    public decimal ClosePrice { get; set; }

    public long Volume { get; set; }

    public DateTime Timestamp { get; set; }
}

