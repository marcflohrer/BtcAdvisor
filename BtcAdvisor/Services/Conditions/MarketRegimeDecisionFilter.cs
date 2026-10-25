using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Conditions;

public static class MarketRegimeDecisionFilter
{
    public static string Apply(BitcoinTradeStatus status, MarketRegime regime)
    {
        return Apply(status, regime, MarketRegimeThresholds.BearishRegimeMinLinearBullishScore);
    }

    public static string Apply(BitcoinTradeStatus status, MarketRegime regime, double bearishRegimeMinLinearBullishScore, bool bullishRegimeBlockBuy = false)
    {
        return Apply(status.TradeAdvice, status.DecisionSource, regime, status.LinearBullishScore, bearishRegimeMinLinearBullishScore, bullishRegimeBlockBuy);
    }

    public static string Apply(
        BitcoinTradeStatus status,
        MarketRegime regime,
        double bearishRegimeMinLinearBullishScore,
        double linearBullishBonus,
        bool bullishRegimeBlockBuy = false)
    {
        return Apply(status.TradeAdvice, status.DecisionSource, regime, status.LinearBullishScore + linearBullishBonus, bearishRegimeMinLinearBullishScore, bullishRegimeBlockBuy);
    }

    public static string Apply(Deal deal, MarketRegime regime)
    {
        return Apply(deal, regime, MarketRegimeThresholds.BearishRegimeMinLinearBullishScore);
    }

    public static string Apply(Deal deal, MarketRegime regime, double bearishRegimeMinLinearBullishScore)
    {
        return Apply(deal.TradeAdvice, deal.DecisionSource, regime, deal.LinearBullishScore, bearishRegimeMinLinearBullishScore);
    }

    public static string Apply(
        Deal deal,
        MarketRegime regime,
        double bearishRegimeMinLinearBullishScore,
        double linearBullishBonus)
    {
        return Apply(deal.TradeAdvice, deal.DecisionSource, regime, deal.LinearBullishScore + linearBullishBonus, bearishRegimeMinLinearBullishScore);
    }

    private static string Apply(
        string advice,
        DecisionSource decisionSource,
        MarketRegime regime,
        double linearBullishScore,
        double bearishRegimeMinLinearBullishScore,
        bool bullishRegimeBlockBuy = false)
    {
        if (advice.Contains("HardStopLoss", StringComparison.OrdinalIgnoreCase))
        {
            return advice;
        }

        if (decisionSource == DecisionSource.Unknown)
        {
            return advice;
        }

        if (bullishRegimeBlockBuy
            && regime == MarketRegime.Bull
            && advice.Contains("Buy", StringComparison.OrdinalIgnoreCase))
        {
            return TradeAdvice.Wait.ToString();
        }

        if (regime == MarketRegime.Bull
            && advice.Contains("Sell", StringComparison.OrdinalIgnoreCase)
            && IsBearishConfluenceDecision(decisionSource))
        {
            return TradeAdvice.Wait.ToString();
        }

        if (regime == MarketRegime.Bear
            && advice.Contains("Buy", StringComparison.OrdinalIgnoreCase)
            && IsBullishDecision(decisionSource)
            && !IsAllowedBearishRegimeBuy(decisionSource, linearBullishScore, bearishRegimeMinLinearBullishScore))
        {
            return TradeAdvice.Wait.ToString();
        }

        return advice;
    }

    private static bool IsBearishConfluenceDecision(DecisionSource decisionSource)
    {
        return decisionSource is DecisionSource.LongTimeframeMultiTrendBearish
            or DecisionSource.LongTimeframeBearishConfluence
            or DecisionSource.LongTimeframeLinearBearish;
    }

    private static bool IsBullishDecision(DecisionSource decisionSource)
    {
        return decisionSource is DecisionSource.LongTimeframeMultiTrendBullish
            or DecisionSource.LongTimeframeBullishConfluence
            or DecisionSource.LongTimeframeLinearBullish;
    }

    private static bool IsAllowedBearishRegimeBuy(
        DecisionSource decisionSource,
        double linearBullishScore,
        double bearishRegimeMinLinearBullishScore)
    {
        return decisionSource == DecisionSource.LongTimeframeLinearBullish
            && linearBullishScore > bearishRegimeMinLinearBullishScore;
    }
}