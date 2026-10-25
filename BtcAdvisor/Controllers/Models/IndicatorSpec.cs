namespace BtcAdvisor.Controllers.Models;

public record IndicatorSpec (string Name, int? Period, bool IsOszillator = true);
