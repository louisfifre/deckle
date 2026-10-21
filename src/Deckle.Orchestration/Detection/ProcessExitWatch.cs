using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Deckle.Orchestration;

// Notices when a watched process exits, without polling: a process handle
// opened with SYNCHRONIZE becomes signaled when the process ends, and the
// thread pool waits on it (RegisterWaitForSingleObject) and calls back once.
// SYNCHRONIZE is granted to a standard user on its own processes, so this
// works with an unelevated Deckle. Protected processes (some anti-cheat) may
// refuse it; their exit then goes unnoticed until Deckle restarts.
public sealed class ProcessExitWatch : IDisposable
{
    private const int ERROR_INVALID_PARAMETER = 87;

    // The refusal is logged as a Warning once per process lifetime; every
    // later refusal gets the Verbose detail.
    private static int _unavailableReported;

    private readonly object _gate = new();
    private readonly Dictionary<int, Registration> _watches = new();
    private bool _disposed;

    /// <summary>
    /// Call <paramref name="onExited"/> once, on a thread-pool thread, when the
    /// process ends. True when the process is watched (or already gone, in which
    /// case the callback runs at once); false when Windows refused the handle.
    /// </summary>
    public bool Watch(int pid, Action<int> onExited)
    {
        ArgumentNullException.ThrowIfNull(onExited);

        lock (_gate)
        {
            if (_disposed) return false;
            if (_watches.ContainsKey(pid)) return true;

            nint raw = ProcessNativeMethods.OpenProcess(ProcessNativeMethods.SYNCHRONIZE, false, pid);
            if (raw != 0)
            {
                var handle = new ProcessWaitHandle(new SafeWaitHandle(raw, ownsHandle: true));
                RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(
                    handle,
                    (_, _) => OnSignaled(pid, onExited),
                    state: null,
                    Timeout.Infinite,
                    executeOnlyOnce: true);
                _watches[pid] = new Registration(handle, registration);
                return true;
            }

            int error = Marshal.GetLastPInvokeError();
            if (error != ERROR_INVALID_PARAMETER)
            {
                LogUnavailable(pid, error, firstTime: Interlocked.Exchange(ref _unavailableReported, 1) == 0);
                return false;
            }
        }

        // ERROR_INVALID_PARAMETER: no process has this id any more. It exited
        // between being seen and being watched; report the exit now, outside
        // the lock, so the callback may call back into this watch.
        NotifyExited(pid, onExited);
        return true;
    }

    /// <summary>Stop watching one process; its callback will not run.</summary>
    public void Unwatch(int pid)
    {
        Registration? removed;
        lock (_gate)
        {
            if (!_watches.Remove(pid, out removed)) return;
        }
        removed.Release();
    }

    /// <summary>Stop watching every process. The instance stays usable.</summary>
    public void UnwatchAll()
    {
        List<Registration> removed;
        lock (_gate)
        {
            removed = [.. _watches.Values];
            _watches.Clear();
        }
        foreach (Registration registration in removed) registration.Release();
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        UnwatchAll();
    }

    // Thread-pool callback. The entry is gone when Unwatch ran first; then the
    // exit is no longer of interest.
    private void OnSignaled(int pid, Action<int> onExited)
    {
        Registration? removed;
        lock (_gate)
        {
            if (!_watches.Remove(pid, out removed)) return;
        }
        removed.Release();
        NotifyExited(pid, onExited);
    }

    // On the thread-pool path an exception escaping the callback terminates
    // the resident process; on the inline path it would surface in the
    // caller of Watch for an exit nobody asked it to handle. Contained on
    // both, so one faulty handler costs one session end, not the app.
    private static void NotifyExited(int pid, Action<int> onExited)
    {
        try
        {
            onExited(pid);
        }
        catch (Exception ex)
        {
            DeckleOrchestrationSource.Log.ExitWatchCallbackFailed(pid, ex.GetType().Name, ex.Message);
        }
    }

    // Internal so the observability test checks what a refusal emits: the
    // Warning the first time, the Verbose detail on every later one.
    internal static void LogUnavailable(int pid, int win32Error, bool firstTime)
    {
        if (firstTime) DeckleOrchestrationSource.Log.ExitWatchUnavailable(pid, win32Error);
        else DeckleOrchestrationSource.Log.ExitWatchUnavailableDetail(pid, win32Error);
    }

    private sealed class Registration(ProcessWaitHandle handle, RegisteredWaitHandle wait)
    {
        // Unregister before closing: the pool must stop waiting on the handle
        // before it is closed under it.
        public void Release()
        {
            wait.Unregister(null);
            handle.Dispose();
        }
    }

    // WaitHandle is abstract; this one only carries the process handle.
    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }
}
