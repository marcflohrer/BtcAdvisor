namespace BtcAdvisor.Services.Conditions;

public static class MarketRegimeThresholds
{
    public const int EmaFastPeriod = 50;
    public const int EmaSlowPeriod = 200;
    public const int AdxPeriod = 14;
    public const double AdxTrendThreshold = 20.0;
    public const double BearishRegimeMinLinearBullishScore = 22.0;
}