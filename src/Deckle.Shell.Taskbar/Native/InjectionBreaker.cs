namespace Deckle.Shell.Taskbar;

// Decides when to stop sending the engine into a target that keeps dying.
//
// The engine runs inside explorer or the Start menu host. Should a build of
// Windows, or a fault of the engine's own, make that process die each time the
// engine arrives, the host would inject again at every rebirth, and the person
// would be left with a taskbar that never comes up. So the host tells this
// class about each injection and each loss of the engine. After
// MaxQuickLosses losses in a row, each within QuickLoss of the injection
// before it, the breaker opens: the host injects no more until Deckle
// restarts, and Windows runs its own taskbar.
//
// A target that outlived QuickLoss starts the count again, so an Explorer
// restart minutes after the injection never counts, whoever caused it.
//
// The figures come from the crashes of 2026-11-09 to 11: Explorer died one to
// three seconds after the injection that killed it, so twenty seconds is a
// wide margin; and a single crash was each time followed by an injection that
// held, so one or two quick losses must not open the breaker.
internal sealed class InjectionBreaker(TimeProvider time)
{
    public static readonly TimeSpan QuickLoss = TimeSpan.FromSeconds(20);
    public const int MaxQuickLosses = 3;

    private readonly object _gate = new();
    private long _injectedAt;
    private bool _injected;      // an injection no loss has been counted against yet
    private int _quickLosses;
    private bool _open;

    /// <summary>Whether the host must stop injecting.</summary>
    public bool IsOpen
    {
        get { lock (_gate) return _open; }
    }

    /// <summary>The engine was sent into the target.</summary>
    public void Injected()
    {
        lock (_gate)
        {
            _injectedAt = time.GetTimestamp();
            _injected = true;
        }
    }

    /// <summary>
    /// The engine was lost without saying it stopped: its target died, or dropped
    /// it. True when this loss opens the breaker, once.
    /// </summary>
    public bool Lost()
    {
        lock (_gate)
        {
            if (_open || !_injected) return false;
            _injected = false;

            if (time.GetElapsedTime(_injectedAt) > QuickLoss)
            {
                _quickLosses = 0;
                return false;
            }

            if (++_quickLosses < MaxQuickLosses) return false;
            _open = true;
            return true;
        }
    }
}
