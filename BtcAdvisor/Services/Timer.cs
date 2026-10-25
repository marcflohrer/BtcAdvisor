using System.Diagnostics;

namespace BtcAdvisor.Services;

public class Timer(Action<TimeSpan> afterMeasuredAction) : IDisposable
{
    private readonly Action<TimeSpan> _afterMeasuredAction = afterMeasuredAction ?? (_ => { });
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    void IDisposable.Dispose()
    {
        _stopwatch.Stop();
        _afterMeasuredAction.Invoke(_stopwatch.Elapsed);
    }
}