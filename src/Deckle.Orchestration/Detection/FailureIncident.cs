namespace Deckle.Orchestration;

// A failure that repeats is one incident: Open() is true for the failure that
// opens it, so only that one is written as a Warning, and Close() is true for
// the success that ends it, so only that one is written as the recovery. Safe
// across threads, since the callers run on the thread pool.
internal sealed class FailureIncident
{
    private int _open;

    public bool Open() => Interlocked.Exchange(ref _open, 1) == 0;

    public bool Close() => Interlocked.Exchange(ref _open, 0) == 1;
}