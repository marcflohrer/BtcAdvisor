namespace BtcAdvisor.Services.Conditions.Models;

/// <summary>
/// Defines the phases of a bull run or flat crash
/// </summary>
public enum StrongTrendPhase
{
    None,
    Starting,
    Accelerating,
    Steady,
    Decelerating,
    Stopped
}