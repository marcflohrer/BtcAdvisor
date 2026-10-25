namespace BtcAdvisor.Services.Indicators;

public interface IIndicatorRegistry
{
    public IIsSignalIndicator GetIndicatorService (string apiId);
}
