using System.Diagnostics;

namespace Deckle.Anytype;

internal interface IBackendTime
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startingTimestamp);
    void Delay(TimeSpan delay, CancellationToken ct);
}

internal sealed class BackendTime : IBackendTime
{
    public long GetTimestamp() => Stopwatch.GetTimestamp();
    public TimeSpan GetElapsedTime(long startingTimestamp) =>
        Stopwatch.GetElapsedTime(startingTimestamp);

    public void Delay(TimeSpan delay, CancellationToken ct)
    {
        if (ct.WaitHandle.WaitOne(delay)) throw new OperationCanceledException(ct);
    }
}
