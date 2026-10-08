using Deckle.Shell.Foreground;

namespace Deckle.Orchestration;

// IForegroundMonitor over the one ForegroundWatcher the App already started.
// It never hooks anything itself: it subscribes to the watcher's Changed and
// translates each relevant snapshot (ForegroundChangeMapping).
//
// The dependency points one way: this module reads Deckle.Shell.Foreground,
// a leaf over Core and Diagnostics, which knows nothing of this module. The
// only Win32 call made here is the image path query this module already owns
// (ProcessNativeMethods.TryGetImagePath); the monitor's device name comes in
// the snapshot, from the watcher's own GetMonitorInfoW call.
//
// Threading. The watcher raises Changed on its hooking thread (the UI
// thread). The mapping and the path query run there; they are a few local
// calls. ForegroundChanged is then raised on the thread pool, because a
// session start applies rules (an HDR switch) and the runtime keeps that off
// the UI thread. Two changes in quick succession may therefore be delivered
// out of order; the runtime only uses them to offer a process to the
// tracker, which ignores a pid it already tracks, so order does not matter.
public sealed class WatcherForegroundMonitor : IForegroundMonitor, IDisposable
{
    // Bounded so a long session cannot grow it without limit; clearing it
    // only means a denied process may be logged once more.
    private const int MaxRememberedDeniedPids = 256;

    private readonly ForegroundWatcher _watcher;

    // Hooking thread only.
    private readonly HashSet<int> _deniedPidsLogged = [];

    private ForegroundChange? _current;
    private bool _disposed;

    public WatcherForegroundMonitor(ForegroundWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        _watcher = watcher;
        _watcher.Changed += OnWatcherChanged;
    }

    public event Action<ForegroundChange>? ForegroundChanged;

    public ForegroundChange? Current => Volatile.Read(ref _current);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.Changed -= OnWatcherChanged;
    }

    private void OnWatcherChanged(ForegroundState state)
    {
        if (_disposed) return;

        ForegroundChange? previous = Volatile.Read(ref _current);
        if (!ForegroundChangeMapping.IsNews(previous, state)) return;

        // A denied query (protected process, or it already exited) still
        // raises the change with a null path: the matcher falls back on the
        // process name, which is how an anti-cheat-protected game is noticed.
        string? path = ProcessNativeMethods.TryGetImagePath((int)state.ProcessId);
        if (path is null) ReportPathUnavailable(state);

        ForegroundChange change = ForegroundChangeMapping.ToChange(state, path);
        Volatile.Write(ref _current, change);
        // A subscriber failure must not end the resident process, as in WmiProcessStartSource.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { ForegroundChanged?.Invoke(change); }
            catch (Exception ex)
            {
                DeckleOrchestrationSource.Log.ForegroundDeliveryFailed(change.ProcessId, ex.GetType().Name, ex.Message);
            }
        });
    }

    // Once per process: an F11 toggle or a monitor move of the same window
    // queries again but does not log again.
    private void ReportPathUnavailable(ForegroundState state)
    {
        int pid = (int)state.ProcessId;
        if (_deniedPidsLogged.Contains(pid)) return;
        if (_deniedPidsLogged.Count >= MaxRememberedDeniedPids) _deniedPidsLogged.Clear();
        _deniedPidsLogged.Add(pid);
        DeckleOrchestrationSource.Log.ForegroundPathUnavailable(pid, state.ProcessName);
    }
}
