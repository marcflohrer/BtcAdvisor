namespace BtcAdvisor.Services.Conditions.Models;

public class WitchSabbathOscillatorConditions
{
    public int NoSellIndicatorCount { get; set; } = 0;
    public int MaxOneSellIndicatorCount { get; set; } = 0;
    public int MoreBuyThanSellCount { get; set; } = 0;
    public int BuyEqualsSellCount { get; set; } = 0;

    public bool MeetsAllConditions()
    {
        return NoSellIndicatorCount >= 3 && MaxOneSellIndicatorCount == 5
            && MoreBuyThanSellCount >= 2 && BuyEqualsSellCount <= 3;
    }
}
