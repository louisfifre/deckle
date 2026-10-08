namespace Deckle.Orchestration;

// A push source of process start events. Start() returns false when the
// source cannot run here (for WMI: Deckle is not elevated); the caller then
// relies on the foreground monitor alone. Started may fire on any thread.
public interface IProcessStartSource : IDisposable
{
    event Action<ProcessStarted>? Started;

    bool Start();

    void Stop();
}
