using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services;

public static class SellFormulaService
{
    // Original algorithm preserved. Only timeframe names were remapped:
    // 5m->2h, 15m->4h, 30m->12h, 1h->1d, 2h->1w.
    private static List<Coefficients> sellCoefficientsForDecision = new List<Coefficients>
    {
        new Coefficients(500000000, 100, 50, 10, "2h"),
        new Coefficients(15, 100, 50, 10, "4h"),
        new Coefficients(30, 100, 50, 10, "12h"),
        new Coefficients(6000, 10000, 50, 10, "1d"),
        new Coefficients(120, 100, 50, -100, "1w"),
    };

    private static List<Coefficients> buyCoefficientsForDecision = new List<Coefficients>
    {
        new Coefficients(5, 100, 50, 10, "2h"),
        new Coefficients(15, 100, 50, 10, "4h"),
        new Coefficients(30, 100, 50, 10, "12h"),
        new Coefficients(60, 100, 50, 10, "1d"),
        new Coefficients(120, 100, 50, 10, "1w"),
    };

    private static readonly long SellBuyAllRatioThreshold = 720135627;
    private static readonly long AllOffset = 48;
    private static readonly long BuyOffset = 6200;

    public static bool IsSellAdvice(
        Dictionary<string, BitcoinTimeRangeStatistics> osciStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics)
    {
        long sell = 0;
        long buy = 0;
        long all = 0;
        var baseRangeSellCount = 0L;      // old 5m
        var baseRangeBuyCount = 0L;
        var secondRangeSellCount = 0L;    // old 15m
        var midRangeSellCount = 0L;       // old 30m
        var midRangeBuyCount = 0L;
        var dayRangeSellCount = 0L;       // old 1h
        var dayRangeBuyCount = 0L;
        var weekRangeSellCount = 0L;      // old 2h

        foreach (var coefficientForDecision in sellCoefficientsForDecision)
        {
            if (osciStatistics.TryGetValue(coefficientForDecision.TimeRange, out var osciStat))
            {
                sell += (long)coefficientForDecision.IndicatorCoefficent * osciStat.IndicatorSellCount
                                * (1 + coefficientForDecision.StrongTrendCoefficient * osciStat.IndicatorStrongTrendCount)
                                * (1 + coefficientForDecision.MediumTrendCoefficient * osciStat.IndicatorMediumTrendCount)
                                * (1 + coefficientForDecision.WeakTrendCoefficient * osciStat.IndicatorWeakTrendCount);
                all += osciStat.IndicatorSellCount
                    + osciStat.IndicatorBuyCount
                    + osciStat.IndicatorStrongTrendCount
                    + osciStat.IndicatorMediumTrendCount
                    + osciStat.IndicatorWeakTrendCount
                    + osciStat.IndicatorNoTrendCount;

                if (coefficientForDecision.TimeRange == "2h")
                {
                    baseRangeSellCount = osciStat.IndicatorSellCount;
                    baseRangeBuyCount = osciStat.IndicatorBuyCount;
                }
                if (coefficientForDecision.TimeRange == "4h")
                {
                    secondRangeSellCount = osciStat.IndicatorSellCount;
                }
                if (coefficientForDecision.TimeRange == "12h")
                {
                    midRangeSellCount = osciStat.IndicatorSellCount;
                    midRangeBuyCount = osciStat.IndicatorBuyCount;
                }
                if (coefficientForDecision.TimeRange == "1d")
                {
                    dayRangeSellCount = osciStat.IndicatorSellCount;
                    dayRangeBuyCount = osciStat.IndicatorBuyCount;
                }
                if (coefficientForDecision.TimeRange == "1w")
                {
                    weekRangeSellCount = osciStat.IndicatorSellCount;
                }
            }

            if (movingAverageStatistics.TryGetValue(coefficientForDecision.TimeRange, out var movingAverageStatistic))
            {
                all += movingAverageStatistic.IndicatorBuyCount
                    + movingAverageStatistic.IndicatorSellCount
                    + movingAverageStatistic.IndicatorStrongTrendCount
                    + movingAverageStatistic.IndicatorMediumTrendCount
                    + movingAverageStatistic.IndicatorWeakTrendCount
                    + movingAverageStatistic.IndicatorNoTrendCount;
            }
        }

        foreach (var coefficientForDecision in buyCoefficientsForDecision)
        {
            if (osciStatistics.TryGetValue(coefficientForDecision.TimeRange, out var osciStat))
            {
                buy += (long)coefficientForDecision.IndicatorCoefficent * osciStat.IndicatorBuyCount
                                * (1 + coefficientForDecision.StrongTrendCoefficient * osciStat.IndicatorStrongTrendCount)
                                * (1 + coefficientForDecision.MediumTrendCoefficient * osciStat.IndicatorMediumTrendCount)
                                * (1 + coefficientForDecision.WeakTrendCoefficient * osciStat.IndicatorWeakTrendCount);
            }
        }

        // ((Sell−(1+Buy))×(All−48))÷(MAX(1;Buy−6200))
        long sellBuyAllRatio = ((sell - (1 + buy)) * (all - AllOffset)) / (Math.Max(1, buy - BuyOffset));
        var isSellAdvice = sellBuyAllRatio >= SellBuyAllRatioThreshold;
        if (isSellAdvice)
        {
            var hasSignature = baseRangeSellCount == 4 && baseRangeBuyCount == 0 ||
                               midRangeSellCount == 4 && midRangeBuyCount == 0 ||
                               baseRangeSellCount == 2 && baseRangeBuyCount == 0 &&
                               dayRangeSellCount == 2 && dayRangeBuyCount == 0;

            if ((baseRangeSellCount < 4 &&
                 secondRangeSellCount < 3 &&
                 baseRangeSellCount != 0 &&
                 secondRangeSellCount != 0) ||
                baseRangeBuyCount > 0 || weekRangeSellCount < 1 ||
                !hasSignature)
            {
                return false;
            }
        }

        return isSellAdvice;
    }
}
