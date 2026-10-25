using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;
using BtcAdvisor.Services.Models.Extensions;

namespace BtcAdvisor.Services;

public static class PrimaryDecisionMaker
{
    private static List<IConditionMet> CreateTradeConditions(LongTimeframeConfluenceThresholdSet thresholdSet) => new List<IConditionMet>()
    {
        new LongTimeframeMultiTrendBearishConditionMet(thresholdSet: thresholdSet),
        new LongTimeframeMultiTrendBullishConditionMet(thresholdSet: thresholdSet),
        new LongTimeframeBearishConfluenceConditionMet(thresholdSet),
        new LongTimeframeBullishConfluenceConditionMet(thresholdSet),
        new LongTimeframeLinearBearishConditionMet(),
        new LongTimeframeLinearBullishConditionMet(thresholdSet: thresholdSet),
    };

    public static BitcoinTradeStatus EvaluateTradeAdvice(
        double currentPrice,
        Dictionary<string, BitcoinTimeRangeStatistics> osciStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        string debugString, DateTime maxDateTime,
        DateTime minDateTime,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? microstrategyHighPrice,
        decimal? microstrategyLowPrice,
        decimal? lowestBuyAfterFireSell,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell)
    {
        return EvaluateTradeAdvice(
            currentPrice,
            osciStatistics,
            movingAverageStatistics,
            debugString,
            maxDateTime,
            minDateTime,
            latestBuyPrice,
            latestSellPrice,
            microstrategyHighPrice,
            microstrategyLowPrice,
            lowestBuyAfterFireSell,
            highestPanicBuyAfterLowestBuyAfterFireSell,
            LongTimeframeConfluenceThresholdSet.Default);
    }

    public static BitcoinTradeStatus EvaluateTradeAdvice(
        double currentPrice,
        Dictionary<string, BitcoinTimeRangeStatistics> osciStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        string debugString, DateTime maxDateTime,
        DateTime minDateTime,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? microstrategyHighPrice,
        decimal? microstrategyLowPrice,
        decimal? lowestBuyAfterFireSell,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet)
    {
        Validate(osciStatistics);
        var tradeAdvice = TradeAdvice.Wait.ToString();
        var decisionSource = DecisionSource.Unknown;
        foreach (var condition in CreateTradeConditions(confluenceThresholdSet))
        {
            if (condition.IsConditionsMet(
                    osciStatistics,
                    movingAverageStatistics,
                    latestBuyPrice,
                    latestSellPrice,
                    Convert.ToDecimal(currentPrice),
                    lowestBuyAfterFireSell,
                    highestPanicBuyAfterLowestBuyAfterFireSell))
            {
                tradeAdvice = condition.TradeAdvice.ToString();
                decisionSource = MapDecisionSource(condition);
                break; // Stop evaluating once a condition is met
            }
        }

        var microstrategyFairRange = MicroStrategyFairValueCalculator.GetFairValue(currentPrice);
        var strategyFairLowRounded = Math.Round(microstrategyFairRange.LowerThreshold);
        var strategyFairHighRounded = Math.Round(microstrategyFairRange.UpperThreshold);
        var timeRangeStatisticsDto = new TimeRangeStatisticsDto(osciStatistics, movingAverageStatistics);
        var shortSellerRisk = GetShortSellerRisk(microstrategyHighPrice, strategyFairLowRounded, strategyFairHighRounded);
        return new BitcoinTradeStatus
        {
            TradeAdvice = tradeAdvice,
            DecisionSource = decisionSource,
            CurrentPrice = currentPrice,
            MicroStrategyLowerThreshold = strategyFairLowRounded,
            MicroStrategyUpperThreshold = strategyFairHighRounded,
            DebugString = $@"{debugString} 

    Strategy Referenz: 
      [${strategyFairLowRounded}, ${strategyFairHighRounded}]
      Aktuell:  [${microstrategyLowPrice}, ${microstrategyHighPrice}]
      ${shortSellerRisk}
    Bitcoin:
      Aktueller Kurs:       {currentPrice}
      Letzter Kaufkurs:     {latestBuyPrice}
      Letzter Verkaufskurs: {latestSellPrice}
      Niedrigister Kaufkurs nach Firesell: {lowestBuyAfterFireSell}
      Höchster Kurs nach niedrigstem Kaufkurs nach Firesell: {highestPanicBuyAfterLowestBuyAfterFireSell}
      Prozentuale Veränderung seit letztem Kauf: {PercentageService.GetPercentage(currentPrice - Convert.ToDouble(latestBuyPrice), Convert.ToDouble(latestBuyPrice))}%
      Prozentuale Veränderung seit letztem Verkauf: {PercentageService.GetPercentage(currentPrice - Convert.ToDouble(latestSellPrice), Convert.ToDouble(latestSellPrice))}%
      Prozentuale Veränderung seit niedrigstem Kaufkurs nach Firesell: {PercentageService.GetPercentage(currentPrice - Convert.ToDouble(lowestBuyAfterFireSell), Convert.ToDouble(lowestBuyAfterFireSell))}%
      Prozentuale Veränderung seit höchstem Kurs nach niedrigstem Kaufkurs nach Firesell: {PercentageService.GetPercentage(currentPrice - Convert.ToDouble(highestPanicBuyAfterLowestBuyAfterFireSell), Convert.ToDouble(highestPanicBuyAfterLowestBuyAfterFireSell))}%

 {timeRangeStatisticsDto.ToTestCase(tradeAdvice)}",
            MaxDateTime = maxDateTime,
            MinDateTime = minDateTime,
        };
    }

    private static DecisionSource MapDecisionSource(IConditionMet condition)
    {
        return condition switch
        {
            LongTimeframeMultiTrendBearishConditionMet => DecisionSource.LongTimeframeMultiTrendBearish,
            LongTimeframeMultiTrendBullishConditionMet => DecisionSource.LongTimeframeMultiTrendBullish,
            LongTimeframeBearishConfluenceConditionMet => DecisionSource.LongTimeframeBearishConfluence,
            LongTimeframeBullishConfluenceConditionMet => DecisionSource.LongTimeframeBullishConfluence,
            LongTimeframeLinearBearishConditionMet => DecisionSource.LongTimeframeLinearBearish,
            LongTimeframeLinearBullishConditionMet => DecisionSource.LongTimeframeLinearBullish,
            _ => DecisionSource.Unknown
        };
    }

    private static string GetShortSellerRisk(decimal? microstrategyHighPrice, double strategyFairLowRounded, double strategyFairHighRounded)
    {
        // Vorabprüfungen
        if (strategyFairLowRounded == 0 || microstrategyHighPrice == null)
        {
            return string.Empty;
        }

        var highPrice = microstrategyHighPrice.Value;
        var fairRange = strategyFairHighRounded - strategyFairLowRounded;
        var relativePosition = (double)highPrice - strategyFairLowRounded;

        // Risikostufen mit Schwellwerten und Beschreibungen
        var riskLevels = new List<(double Threshold, string Description)>
        {
            (0.0, "=> Sehr geringes Short-Seller-Risiko"),
            (0.1, "=> Geringes Short-Seller-Risiko"),
            (0.25, "=> Beginnendes Short-Seller-Risiko"),
            (0.5, "=> Mittleres Short-Seller-Risiko"),
            (0.75, "=> Short-Seller-Einstieg immer wahrscheinlicher"),
            (0.9, "=> Short-Seller-Einstieg jederzeit möglich.")
        };

        // Bestimme die passende Risikostufe
        foreach (var (threshold, description) in riskLevels)
        {
            if (relativePosition <= fairRange * threshold)
            {
                return description;
            }
        }

        // Falls kein Schwellwert passt (z.B. wenn relativePosition > fairRange)
        return "Short-Seller-Einstieg sehr wahrscheinlich bald.";
    }


    private static void Validate(Dictionary<string, BitcoinTimeRangeStatistics> statistics)
    {
        var count = 0;
        foreach(var kvp in statistics)
        {
            count +=  kvp.Value.IndicatorSellCount;
            count +=  kvp.Value.IndicatorNeutralCount;
            count +=  kvp.Value.IndicatorBuyCount;
            count +=  kvp.Value.IndicatorStrongTrendCount;
            count +=  kvp.Value.IndicatorMediumTrendCount;
            count +=  kvp.Value.IndicatorWeakTrendCount;
            count +=  kvp.Value.IndicatorNoTrendCount;
            count +=  kvp.Value.IndicatorNotFound;
        }

        if (count == 0)
        {
            Console.WriteLine($"{DateTime.UtcNow.ToString()} ERROR: not a single non-zero entry in oscillator statistics.");
        }
    }
}
