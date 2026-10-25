namespace BtcAdvisor.Services.Conditions;

/// <summary>
/// Minimum directional price follow-through required between accepted same-direction raw signals.
/// Values are calibrated with the insights follow-through sweep and applied consistently in
/// realtime handling and historical backtests.
/// </summary>
public static class SignalFollowThroughThresholds
{
    public const double BullishMinFollowThroughPercent = 0.0;
    public const double BearishMinFollowThroughPercent = 0.0;
}