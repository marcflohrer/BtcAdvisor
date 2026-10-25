namespace BtcAdvisor.Services.Models;

public enum DecisionSource
{
    Unknown = 0,
    LongTimeframeMultiTrendBearish = 1,
    LongTimeframeMultiTrendBullish = 2,
    LongTimeframeBearishConfluence = 3,
    LongTimeframeBullishConfluence = 4,
    LongTimeframeLinearBearish = 5,
    LongTimeframeLinearBullish = 6,
    DistributionTop = 7,
    HardStopLoss = 8,
}