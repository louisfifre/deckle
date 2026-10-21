using static Deckle.Shell.Foreground.ForegroundNativeMethods;

namespace Deckle.Shell.Foreground;

// The process-wide, event-driven view of the foreground window: which window
// and process it is, which monitor it sits on, whether it reads as
// fullscreen. Consumers read Current from any thread or subscribe to Changed.
//
// Two WinEvent hooks, event driven only:
//   • EVENT_SYSTEM_FOREGROUND, global — another window comes forward;
//   • EVENT_OBJECT_LOCATIONCHANGE, narrowed to the foreground window's
//     process and re-armed on each switch — the foreground window moves or
//     resizes in place (an F11 toggle raises no foreground event). Narrowed
//     so the process keeps a single global location stream, the taskbar
//     cover's cursor hook, instead of a second one at input cadence.
//
// Threading. Both hooks deliver on the thread that called Start, which must
// pump messages (the UI thread in Deckle.App, like CursorMovementSignal).
// Start, Stop and Dispose must run on that thread: UnhookWinEvent fails from
// any other. Changed is raised on it. Current is an immutable record swapped
// by reference, safe to read from any thread.
public sealed partial class ForegroundWatcher : IDisposable
{
    private IntPtr _foregroundHook;
    private IntPtr _locationHook;
    private uint _locationHookProcessId;
    private bool _locationFailureReported;
    private bool _started;

    // Rooted for the GC while native code holds their function pointers.
    private WinEventProc? _foregroundProc;
    private WinEventProc? _locationProc;

    private ForegroundState _current = ForegroundState.None;

    /// <summary>
    /// The latest foreground snapshot. Never null; <see cref="ForegroundState.None"/>
    /// before <see cref="Start"/> and after <see cref="Stop"/>.
    /// </summary>
    public ForegroundState Current => Volatile.Read(ref _current);

    /// <summary>
    /// Raised on the hooking thread each time a new snapshot is published:
    /// a foreground switch, or the foreground window changing monitor or
    /// fullscreen stage in place.
    /// </summary>
    public event Action<ForegroundState>? Changed;

    /// <summary>
    /// Hooks the foreground signal and publishes the first snapshot. Call on
    /// a thread that pumps messages. Returns false (and logs) when the
    /// foreground hook could not be set; a failed location hook only logs.
    /// </summary>
    public bool Start()
    {
        if (_started) return true;

        _foregroundProc = OnForegroundEvent;
        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_foregroundHook == IntPtr.Zero)
        {
            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            _foregroundProc = null;
            DeckleShellForegroundSource.Log.WatcherStartFailed("foreground_hook_failed", error);
            return false;
        }

        _started = true;
        _locationProc = OnLocationChange;
        DeckleShellForegroundSource.Log.WatcherStarted();

        var state = ProbeWindow(Deckle.Core.NativeMethods.GetForegroundWindow());
        ArmLocationHook(state.ProcessId);
        Publish(state, "start");
        return true;
    }

    /// <summary>Unhooks both hooks. Call on the thread that called Start.</summary>
    public void Stop()
    {
        if (!_started) return;
        _started = false;

        DisarmLocationHook();
        UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
        _foregroundProc = null;
        _locationProc = null;
        _locationFailureReported = false;

        Volatile.Write(ref _current, ForegroundState.None);
        DeckleShellForegroundSource.Log.WatcherStopped();
    }

    public void Dispose() => Stop();

    // ── Hook callbacks (hooking thread) ───────────────────────────────────

    private void OnForegroundEvent(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (!_started || idObject != OBJID_WINDOW) return;
        if (hwnd == _current.Hwnd) return;

        var state = ProbeWindow(hwnd);
        if (state.ProcessId != _locationHookProcessId)
        {
            DisarmLocationHook();
            ArmLocationHook(state.ProcessId);
        }
        Publish(state, "foreground");
    }

    // Fires for every object of the foreground process that moves (child
    // windows, carets, scrollbars): only the window itself counts.
    private void OnLocationChange(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (!_started) return;
        var current = _current;
        if (hwnd != current.Hwnd || hwnd == IntPtr.Zero
            || idObject != OBJID_WINDOW || idChild != 0) return;

        var placed = ProbePlacement(current);
        if (placed == current) return;
        Publish(placed, "location");
    }

    // ── Location hook, narrowed to one process ─────────────────────────────

    private void ArmLocationHook(uint processId)
    {
        // idProcess == 0 would hook every process on the desktop — the very
        // global stream this narrowing exists to avoid. No known process, no
        // location hook until the next foreground switch.
        if (processId == 0 || _locationProc is null) return;

        _locationHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _locationProc, processId, 0, WINEVENT_OUTOFCONTEXT);
        if (_locationHook != IntPtr.Zero)
        {
            _locationHookProcessId = processId;
            if (_locationFailureReported)
            {
                _locationFailureReported = false;
                DeckleShellForegroundSource.Log.HookRecovered("location");
            }
            return;
        }

        int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        if (!_locationFailureReported)
        {
            DeckleShellForegroundSource.Log.HookFailed("location", error);
            _locationFailureReported = true;
        }
        DeckleShellForegroundSource.Log.HookFailedDetail("location", processId, error);
    }

    private void DisarmLocationHook()
    {
        if (_locationHook != IntPtr.Zero) UnhookWinEvent(_locationHook);
        _locationHook = IntPtr.Zero;
        _locationHookProcessId = 0;
    }

    // ── Publish ───────────────────────────────────────────────────────────

    private void Publish(ForegroundState state, string reason)
    {
        var previous = _current;
        Volatile.Write(ref _current, state);

        // One detail line per switch or fullscreen change — never one per
        // in-place move that only changes the monitor.
        if (reason != "location" || state.Fullscreen != previous.Fullscreen)
        {
            DeckleShellForegroundSource.Log.ForegroundChanged(
                state.ProcessName, StageName(state.Fullscreen), reason);
        }

        Changed?.Invoke(state);
    }

    private static string StageName(FullscreenStage stage) => stage switch
    {
        FullscreenStage.NotificationState => "notification_state",
        FullscreenStage.Geometry          => "geometry",
        _                                 => "none",
    };
}
