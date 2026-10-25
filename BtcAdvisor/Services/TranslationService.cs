using BtcAdvisor.Services.Conditions.Models;

namespace BtcAdvisor.Services;

public static class TranslationService
{
    public static string Translate(string advice)
    {
        switch (advice)
        {
            case "PanicBuy":
                return "Jetzt kaufen! (experimentell - formula)";
            case "PanicBuy2k":
                return "Jetzt kaufen! (experimentell - ⇧ $2000)";
            case "PanicBuyStrict":
                return "Jetzt kaufen! (experimentell - strict)";
            case "PanicBuyStrong": 
                return "Jetzt kaufen! (experimentell - strong)";
            case "StrongBuy": 
                return "Jetzt kaufen! (verschärfte Regeln)";
            case "StrictBuy":
                return "Kaufen (verschärfte Regeln)";
            case "Buy2k": 
                return "Kaufen (⇧ $2000)";
            case "Buy": 
                return "Kaufen (neue Formel)";
            case "HiddenBuy": 
                return "Kaufen (Versteckt)";
            case "WitchSabbathBuy": 
                return "Kaufen? (StartBuy 1% über aktuellem Kurs setzen)";
            case "StrictSell": 
                return "Verkaufen (verschärft)";
            case "Sell": 
                return "Verkaufen (neue Formel)";
            case "StrictSell1k": 
                return "Abwärtstrend erkannt (verschärft) (⬇ $1000)";
            case "StrictSell500": 
                return "Verkaufen (verschärft) (⬇ $500)";
            case "HalloweenSell": 
                return "Verkaufen (Halloween)";
            case "StrongSell": 
                return "Verkaufen (starkes Signal)";
            case "FireSellStrict":
                return "Jetzt verkaufen! (verschärft)";
            case "FireSellStrong": 
                return "Jetzt verkaufen! (strong)";
            case "PanicBuyWitchSabbath":
                return "Jetzt kaufen! (Hexensabat)";
            case "PanicBuyHidden":
                return "Jetzt kaufen! (versteckt)";
            case "FireSellFormula":
                return "Jetzt verkaufen! (neue Formel)";
            case "FireSellHalloween":
                return "Jetzt verkaufen! (Halloween)";
            case "FireSell1k":
                return "Jetzt verkaufen! (⬇ 1000$)";
            case "FireSell500":
                return "Jetzt verkaufen! (⬇ 500$)";
            case "Wait":
                return "Warten";
            default:
                return advice;
        }
    }

    public static string TranslateStrongTrendPhase(StrongTrendPhase strongTrendPhase)
    {
        switch (strongTrendPhase)
        {
            case StrongTrendPhase.None: 
                return "Keine Phase";
            case StrongTrendPhase.Accelerating:
                return "Beschleunigend";
            case StrongTrendPhase.Starting: 
                return "Am Anfang";
            case StrongTrendPhase.Steady: 
                return "Stabil";
            case StrongTrendPhase.Decelerating: 
                return "Abschwächend";
            case StrongTrendPhase.Stopped: 
                return "Gestoppt";
            default:
                return "Unbekannte Phase";
        }
    }
}