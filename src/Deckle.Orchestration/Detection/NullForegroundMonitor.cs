namespace Deckle.Orchestration;

// Stand-in for tests and for a composition without the shared watcher: never
// raises, knows no current window. With it, only process start events and the
// one-shot enumeration on each reconcile notice an app.
public sealed class NullForegroundMonitor : IForegroundMonitor
{
    // Never raised; the empty accessors keep the compiler from warning about it.
    public event Action<ForegroundChange>? ForegroundChanged
    {
        add { }
        remove { }
    }

    public ForegroundChange? Current => null;
}
