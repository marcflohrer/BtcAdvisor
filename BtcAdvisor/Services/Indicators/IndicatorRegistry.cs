namespace BtcAdvisor.Services.Indicators;

public class IndicatorRegistry : IIndicatorRegistry
{
    private readonly List<IIsSignalIndicator> SignalIndicators = [
        new ADXIndicator (),
        new AwesomeOscillator (),
        new CCIIndicator (),
        new EMAIndicator (),
        new HullMovingAverageIndicator (),
        new IchimokuIndicator (),
        new KickingIndicator (),
        new MacdIndicator (),
        new MomentumIndicator (),
        new RelativeStabilityIndex (),
        new SMAIndicator (),
        new Stochastic (),
        new StochasticFastIndicator (),
        new WilliamsRIndicator (),
    ];

    public IIsSignalIndicator GetIndicatorService (string apiId)
    {
        foreach (var isi in SignalIndicators)
        {
            if (isi is ICanHandle isiCanHandle)
            {
                if (isiCanHandle.CanHandle (apiId))
                {
                    return isi;
                }
            }
        }

        throw new NotImplementedException ($"Indicator {apiId} is not implemented.");
    }
}
