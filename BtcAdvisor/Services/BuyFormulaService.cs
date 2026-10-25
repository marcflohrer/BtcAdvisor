using BtcAdvisor.Repository.Models;
using BtcAdvisor.Repository;

namespace BtcAdvisor.Services;

public static class BuyFormulaService
{
    private static readonly List<Coefficients> osciSellCoefficientsLegacy = new()
    {
        new Coefficients(500, 100, 50, 10, "5m"),
        new Coefficients(15, 100, 50, 10, "15m"),
        new Coefficients(30, 100, 50, 10, "30m"),
        new Coefficients(60, 100, 50, 10, "1h"),
        new Coefficients(120, 100, 50, -100, "2h"),
    };

    private static readonly List<Coefficients> osciSellCoefficientsLong = new()
    {
        new Coefficients(500, 100, 50, 10, "2h"),
        new Coefficients(15, 100, 50, 10, "4h"),
        new Coefficients(30, 100, 50, 10, "12h"),
        new Coefficients(60, 100, 50, 10, "1d"),
        new Coefficients(120, 100, 50, -100, "1w"),
    };

    private static readonly List<Coefficients> osciBuyCoefficientsLegacy = new()
    {
        new Coefficients(500, 100, 50, 10, "5m"),
        new Coefficients(15, 100, 50, 10, "15m"),
        new Coefficients(30, 100, 50, 10, "30m"),
        new Coefficients(60, 100, 50, 10, "1h"),
        new Coefficients(120, 100, 50, 10, "2h"),
    };

    private static readonly List<Coefficients> osciBuyCoefficientsLong = new()
    {
        new Coefficients(500, 100, 50, 10, "2h"),
        new Coefficients(15, 100, 50, 10, "4h"),
        new Coefficients(30, 100, 50, 10, "12h"),
        new Coefficients(60, 100, 50, 10, "1d"),
        new Coefficients(120, 100, 50, 10, "1w"),
    };

    private static readonly List<Coefficients> movingAverageSellCoefficientsLegacy = new()
    {
        new Coefficients(5, 0, 0, 0, "5m"),
        new Coefficients(15, 0, 0, 0, "15m"),
        new Coefficients(30, 0, 0, 0, "30m"),
        new Coefficients(60, 0, 0, 0, "1h"),
        new Coefficients(120, 0, 0, 0, "2h"),
    };

    private static readonly List<Coefficients> movingAverageSellCoefficientsLong = new()
    {
        new Coefficients(5, 0, 0, 0, "2h"),
        new Coefficients(15, 0, 0, 0, "4h"),
        new Coefficients(30, 0, 0, 0, "12h"),
        new Coefficients(60, 0, 0, 0, "1d"),
        new Coefficients(120, 0, 0, 0, "1w"),
    };

    private static readonly List<Coefficients> movingAverageBuyCoefficientsLegacy = new()
    {
        new Coefficients(5000, 0, 0, 0, "5m"),
        new Coefficients(15, 0, 0, 0, "15m"),
        new Coefficients(30, 0, 0, 0, "30m"),
        new Coefficients(60, 0, 0, 0, "1h"),
        new Coefficients(120, 0, 0, 0, "2h"),
    };

    private static readonly List<Coefficients> movingAverageBuyCoefficientsLong = new()
    {
        new Coefficients(5000, 0, 0, 0, "2h"),
        new Coefficients(15, 0, 0, 0, "4h"),
        new Coefficients(30, 0, 0, 0, "12h"),
        new Coefficients(60, 0, 0, 0, "1d"),
        new Coefficients(120, 0, 0, 0, "1w"),
    };

    private static readonly long buySellSumThreshold = 34040;

    public static bool IsBuyAdvice(
        Dictionary<string, BitcoinTimeRangeStatistics> osciStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics)
    {
        var isLongProfile = ResolveLongProfileFromInput(osciStatistics, movingAverageStatistics);
        var baseRange = isLongProfile ? "2h" : "5m";
        var secondRange = isLongProfile ? "4h" : "15m";
        var dayRange = isLongProfile ? "1d" : "1h";
        var osciSellCoefficientsForDecision = isLongProfile ? osciSellCoefficientsLong : osciSellCoefficientsLegacy;
        var osciBuyCoefficientsForDecision = isLongProfile ? osciBuyCoefficientsLong : osciBuyCoefficientsLegacy;
        var movingAverageSellCoefficientsForDecision = isLongProfile ? movingAverageSellCoefficientsLong : movingAverageSellCoefficientsLegacy;
        var movingAverageBuyCoefficientsForDecision = isLongProfile ? movingAverageBuyCoefficientsLong : movingAverageBuyCoefficientsLegacy;

        long buySellSum = 0;
        var baseRangeSellCount = 0L;
        var baseRangeBuyCount = 0L;
        var secondRangeBuyCount = 0L;
        var dayRangeSellCount = 0L;
        var dayRangeBuyCount = 0L;
        var dayRangeStrongTrend = false;

        foreach (var coefficientForDecision in osciSellCoefficientsForDecision)
        {
            if (osciStatistics.TryGetValue(coefficientForDecision.TimeRange, out var osciStat))
            {
                buySellSum -= (long)coefficientForDecision.IndicatorCoefficent * osciStat.IndicatorSellCount
                              * (1 + coefficientForDecision.StrongTrendCoefficient * osciStat.IndicatorStrongTrendCount)
                              * (1 + coefficientForDecision.MediumTrendCoefficient * osciStat.IndicatorMediumTrendCount)
                              * (1 + coefficientForDecision.WeakTrendCoefficient * osciStat.IndicatorWeakTrendCount);

                if (coefficientForDecision.TimeRange == baseRange)
                {
                    baseRangeSellCount = osciStat.IndicatorSellCount;
                    baseRangeBuyCount = osciStat.IndicatorBuyCount;
                }

                if (coefficientForDecision.TimeRange == secondRange)
                {
                    secondRangeBuyCount = osciStat.IndicatorBuyCount;
                }

                if (coefficientForDecision.TimeRange == dayRange)
                {
                    dayRangeSellCount = osciStat.IndicatorSellCount;
                    dayRangeBuyCount = osciStat.IndicatorBuyCount;
                    dayRangeStrongTrend = osciStat.IndicatorStrongTrendCount == 1;
                }
            }
        }

        foreach (var coefficientForDecision in movingAverageSellCoefficientsForDecision)
        {
            if (movingAverageStatistics.TryGetValue(coefficientForDecision.TimeRange, out var movingAverageStatistic))
            {
                buySellSum -= coefficientForDecision.IndicatorCoefficent * movingAverageStatistic.IndicatorSellCount;
            }
        }

        foreach (var coefficientForDecision in osciBuyCoefficientsForDecision)
        {
            if (osciStatistics.TryGetValue(coefficientForDecision.TimeRange, out var osciStat))
            {
                buySellSum += (long)coefficientForDecision.IndicatorCoefficent * osciStat.IndicatorBuyCount
                              * (1 + coefficientForDecision.StrongTrendCoefficient * osciStat.IndicatorStrongTrendCount)
                              * (1 + coefficientForDecision.MediumTrendCoefficient * osciStat.IndicatorMediumTrendCount)
                              * (1 + coefficientForDecision.WeakTrendCoefficient * osciStat.IndicatorWeakTrendCount);
            }
        }

        foreach (var coefficientForDecision in movingAverageBuyCoefficientsForDecision)
        {
            if (movingAverageStatistics.TryGetValue(coefficientForDecision.TimeRange, out var movingAverageStatistic))
            {
                buySellSum += coefficientForDecision.IndicatorCoefficent * movingAverageStatistic.IndicatorBuyCount;
            }
        }

        var firstKnockOutRule = baseRangeSellCount != 0;
        var secondKnockOutRule = baseRangeBuyCount + baseRangeSellCount == 0;
        var thirdKnockoutRule = dayRangeBuyCount + dayRangeSellCount == 0 && dayRangeStrongTrend;
        var fourthKnockoutRule = secondRangeBuyCount > 2;

        var isBuyAdvice = buySellSum >= buySellSumThreshold;

        return isBuyAdvice && !firstKnockOutRule && !secondKnockOutRule && !thirdKnockoutRule && !fourthKnockoutRule;
    }

    private static bool ResolveLongProfileFromInput(
        Dictionary<string, BitcoinTimeRangeStatistics> osciStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics)
    {
        var hasLongKeys = HasAnyKey(osciStatistics, movingAverageStatistics, ["4h", "12h", "1d", "1w"]);
        var hasLegacyKeys = HasAnyKey(osciStatistics, movingAverageStatistics, ["5m", "15m", "30m"]);

        if (hasLongKeys && !hasLegacyKeys)
        {
            return true;
        }

        if (hasLegacyKeys && !hasLongKeys)
        {
            return false;
        }

        return LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe;
    }

    private static bool HasAnyKey(
        Dictionary<string, BitcoinTimeRangeStatistics> osciStatistics,
        Dictionary<string, BitcoinTimeRangeStatistics> movingAverageStatistics,
        IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            if (osciStatistics.ContainsKey(key) || movingAverageStatistics.ContainsKey(key))
            {
                return true;
            }
        }

        return false;
    }
}

public record Coefficients(int IndicatorCoefficent, int StrongTrendCoefficient, int MediumTrendCoefficient, int WeakTrendCoefficient, string TimeRange);
