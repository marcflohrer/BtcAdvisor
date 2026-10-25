namespace BtcAdvisor.Services.Conditions.Models;

public class WitchSabbathMovingAverageConditions
{
    public int NoSellIndicatorCount { get; set; } = 0;
    public int MinFiveBuyIndicatorCount { get; set; } = 0;
    public int MinSixBuyIndicatorCount { get; set; } = 0;
    public int MinSevenBuyAndNoSellCount { get; set; } = 0;

    public bool MeetsAllConditions()
    {
        return NoSellIndicatorCount >= 3 && MinFiveBuyIndicatorCount == 5
            && MinSixBuyIndicatorCount >= 4 && MinSevenBuyAndNoSellCount >= 3;
    }
}
