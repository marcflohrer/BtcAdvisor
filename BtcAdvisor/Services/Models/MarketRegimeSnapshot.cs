namespace BtcAdvisor.Services.Models;

public sealed record MarketRegimeSnapshot(
    MarketRegime Regime,
    double Close,
    double? Ema50,
    double? Ema200,
    double? Adx);