using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

// Dedicated thread owning the cover band — an opaque black topmost window
// sized to the exact taskbar rect — and the whole reveal state machine:
//
//   • the band covers the taskbar whatever edge it is anchored to
//     (ABM_GETTASKBARPOS rect, rebuilt on WM_SETTINGCHANGE /
//     WM_DISPLAYCHANGE / TaskbarCreated);
//   • the cursor entering the reveal zone hides the band instantly;
//     leaving it re-covers after RecoverDelayMs;
//   • a fullscreen / presentation foreground app suppresses the band;
//     two WinEvent signals reconcile it — and the band's z-order above the
//     taskbar — the instant it happens: a foreground change (another app
//     comes forward) and a location change on the foreground window itself
//     (an in-place F11 toggle, which raises no foreground event). No poll;
//   • sleep and session lock park the machine entirely.
//
// Cursor movement arrives through a WinEvent hook
// (EVENT_OBJECT_LOCATIONCHANGE filtered on OBJID_CURSOR), not Raw Input:
// the RIDEV_INPUTSINK mouse registration is per-process-per-usage and
// CursorMovementSignal (Deckle.Shell) owns the only one. The hook is asynchronous
// and additive — no input-chain latency, no contention. It delivers on
// the thread that registered it, which therefore owns a message pump;
// the same thread owns the window, the timers and every state field, so
// the machine runs without a single lock. Start/Stop mirror
// RawInputHost's thread lifecycle.
public sealed class TaskbarCoverHost : IDisposable
{
    private const string ClassName = "DeckleTaskbarCover";

    // Delay before the band re-covers the taskbar once the cursor has left
    // the reveal zone. Ported from the standalone utility (HIDE_DELAY),
    // calibrated in daily use; a constant, not a setting.
    public const uint RecoverDelayMs = 5000;

    private const uint TIMER_RECOVER_ID = 1;
    private static readonly UIntPtr TIMER_RECOVER = new(TIMER_RECOVER_ID);

    private readonly object _stateLock = new();

    private Thread? _thread;
    // Worker that outlived its Join in Stop() — it still owns the window
    // class, the delegates and the native handles, so Start() refuses to
    // run over it until it has fully exited and torn itself down.
    private Thread? _defunctThread;
    private uint _threadId;
    private IntPtr _hwnd;
    private ushort _classAtom;
    private IntPtr _hInstance;
    private IntPtr _brush;
    private IntPtr _cursorHook;
    private IntPtr _foregroundHook;
    private uint _wmTaskbarCreated;

    // Rooted for the GC while native code holds their function pointers.
    private NativeMethods.WndProc? _wndProcDelegate;
    private WinEventProc? _cursorHookDelegate;
    private WinEventProc? _foregroundHookDelegate;

    private volatile bool _running;

    // ── State machine — worker thread only ───────────────────────────────
    // _coverVisible       : current band window state (sole writer: UpdateCover)
    // _cursorInRevealZone : the taskbar is revealed; pessimistic at boot,
    //                       corrected from the real cursor once layout is known
    // _recoverTimerArmed  : between zone exit and RecoverDelayMs expiry
    // _appSuppressed      : a fullscreen/presentation app is foreground
    // _systemSuspended    : sleep or locked session — machine parked
    private bool _coverVisible;
    private bool _cursorInRevealZone = true;
    private bool _recoverTimerArmed;
    private bool _appSuppressed;
    private bool _systemSuspended;

    // Last foreground window — tracked by the foreground hook so the
    // location-change hook can recognise an in-place resize of *that* window
    // (the F11 fullscreen toggle) with a pointer compare, no syscall on the
    // input-cadence path.
    private IntPtr _foregroundHwnd;

    private bool _layoutKnown;
    private bool _layoutFailureLogged;
    private bool _recoverArmFailureLogged;
    private TaskbarEdge _edge;
    private NativeMethods.RECT _band;
    private NativeMethods.RECT _zone;

    public bool IsRunning => _running;

    /// <summary>
    /// Spawns the cover thread, creates the band window, hooks the cursor.
    /// Returns false (and logs) when the native setup failed; the app keeps
    /// running without the cover.
    /// </summary>
    public bool Start()
    {
        lock (_stateLock)
        {
            if (_running) return true;

            if (_defunctThread is { } defunct)
            {
                // Join(0) succeeding means the stuck worker completed its own
                // TearDown and exited: the shared native fields are clean
                // again. Until then, restarting would corrupt them.
                if (!defunct.Join(TimeSpan.Zero))
                {
                    DeckleShellTaskbarCoverSource.Log.HostStartFailed();
                    DeckleShellTaskbarCoverSource.Log.HostStartFailedDetail(
                        "DefunctWorker", "previous cover thread has not exited yet");
                    return false;
                }
                _defunctThread = null;
            }

            using var ready = new ManualResetEventSlim(false);
            Exception? startError = null;
            bool started = false;

            _thread = new Thread(() =>
            {
                try
                {
                    SetupWindow();
                    started = true;
                }
                catch (Exception ex)
                {
                    startError = ex;
                    TearDown();
                }
                finally
                {
                    ready.Set();
                }

                if (started)
                {
                    BootstrapState();
                    RunPump();
                    TearDown();
                }
            })
            {
                Name = "Deckle TaskbarCover",
                IsBackground = true,
            };
            _thread.Start();
            ready.Wait();

            if (!started)
            {
                DeckleShellTaskbarCoverSource.Log.HostStartFailed();
                DeckleShellTaskbarCoverSource.Log.HostStartFailedDetail(
                    startError?.GetType().Name ?? "(unknown)", startError?.Message ?? "(no message)");
                _thread = null;
                return false;
            }

            _running = true;
            return true;
        }
    }

    /// <summary>Posts WM_QUIT to the cover thread and joins it.</summary>
    public void Stop()
    {
        Thread? thread;
        uint threadId;
        lock (_stateLock)
        {
            if (!_running || _thread is null) return;
            _running = false;
            thread = _thread;
            threadId = _threadId;
            _thread = null;
        }

        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (!thread.Join(TimeSpan.FromSeconds(3)))
        {
            // Stuck worker — realistically SHAppBarMessage sent to a hung
            // Explorer. The queued WM_QUIT will kill it the moment the send
            // returns; until then Start() must not reuse its native state.
            lock (_stateLock) { _defunctThread = thread; }
            DeckleShellTaskbarCoverSource.Log.HostStopTimedOut();
            return;
        }
        DeckleShellTaskbarCoverSource.Log.HostStopped();
    }

    public void Dispose() => Stop();

    // ── Worker thread ─────────────────────────────────────────────────────

    private void SetupWindow()
    {
        _threadId = GetCurrentThreadId();

        _hInstance = NativeMethods.GetModuleHandle(null);
        if (_hInstance == IntPtr.Zero)
            throw new InvalidOperationException(
                $"GetModuleHandle failed (Win32 err {Marshal.GetLastWin32Error()})");

        _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        _wndProcDelegate = WndProc;
        _brush = CreateSolidBrush(0x000000);

        var wc = new WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance     = _hInstance,
            hbrBackground = _brush,
            lpszClassName = ClassName,
        };

        _classAtom = NativeMethods.RegisterClassEx(ref wc);
        if (_classAtom == 0)
            throw new InvalidOperationException(
                $"RegisterClassEx('{ClassName}') failed (Win32 err {Marshal.GetLastWin32Error()})");

        // NOACTIVATE + TOOLWINDOW: the band never takes focus and never
        // appears in Alt-Tab. Not click-through — a click on the band is
        // deliberately swallowed, the taskbar below stays masked.
        // TOPMOST at creation, not via a later SetWindowPos: promoting an
        // existing window to topmost needs SetForegroundWindow permission,
        // which the process lacks while another app owns the foreground (the
        // common case at boot) — the call succeeds yet silently leaves the band
        // below the taskbar. Born topmost, it isn't subject to that gate; the
        // SetWindowPos(HWND_TOPMOST) calls then only restack an already-topmost
        // window, which carries no permission requirement.
        _hwnd = NativeMethods.CreateWindowEx(
            dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST,
            lpClassName:  ClassName,
            lpWindowName: null,
            dwStyle:      WS_POPUP,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent:   IntPtr.Zero,
            hMenu:        IntPtr.Zero,
            hInstance:    _hInstance,
            lpParam:      IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                $"CreateWindowEx failed (Win32 err {Marshal.GetLastWin32Error()})");

        // Without the cursor signal the band would cover the taskbar and
        // never reveal it — a dead hook fails the whole start.
        _cursorHookDelegate = OnLocationChange;
        _cursorHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _cursorHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_cursorHook == IntPtr.Zero)
        {
            throw new InvalidOperationException("SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE) failed");
        }

        // Foreground changes drive the immediate reconciliation of fullscreen
        // suppression and z-order. Non-fatal, unlike the cursor hook: on
        // failure the 5 s poll alone still does the job, just lazily — so the
        // band never gets stuck, it just reacts slower.
        _foregroundHookDelegate = OnForegroundEvent;
        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_foregroundHook == IntPtr.Zero)
            DeckleShellTaskbarCoverSource.Log.ForegroundHookFailed();

        if (!WTSRegisterSessionNotification(_hwnd, NOTIFY_FOR_THIS_SESSION))
            DeckleShellTaskbarCoverSource.Log.SessionNotifyFailed();

        DeckleShellTaskbarCoverSource.Log.HostStarted();
        DeckleShellTaskbarCoverSource.Log.HostStartedDetail(_hwnd.ToInt64(), (int)_threadId);
    }

    // First layout and gate probes, off Start()'s critical path: the gated
    // section (SetupWindow) makes local calls exclusively — the profile
    // RawInputHost set — while SHAppBarMessage is a synchronous send to
    // Explorer's tray window. Against a hung Explorer it blocks this thread
    // only, never the caller of Start().
    private void BootstrapState()
    {
        RebuildLayout("boot");
        ResetZoneStateFromCursor();
        _foregroundHwnd = NativeMethods.GetForegroundWindow();
        EvaluateAppSuppressed();

        UpdateCover("boot");
    }

    private void RunPump()
    {
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private void TearDown()
    {
        if (_cursorHook != IntPtr.Zero)
        {
            // Must run on the thread that called SetWinEventHook — we are on it.
            UnhookWinEvent(_cursorHook);
            _cursorHook = IntPtr.Zero;
        }
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        if (_hwnd != IntPtr.Zero)
        {
            if (_recoverTimerArmed) KillTimer(_hwnd, TIMER_RECOVER);
            WTSUnRegisterSessionNotification(_hwnd);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        if (_classAtom != 0)
        {
            NativeMethods.UnregisterClass(ClassName, _hInstance);
            _classAtom = 0;
        }
        if (_brush != IntPtr.Zero)
        {
            // UnregisterClass does not free class brushes.
            DeleteObject(_brush);
            _brush = IntPtr.Zero;
        }

        // Re-arm the boot-pessimistic state for a potential restart.
        _coverVisible       = false;
        _cursorInRevealZone = true;
        _recoverTimerArmed  = false;
        _appSuppressed      = false;
        _systemSuspended    = false;
        _foregroundHwnd     = IntPtr.Zero;
        _layoutKnown        = false;
        _layoutFailureLogged = false;
        _recoverArmFailureLogged = false;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_wmTaskbarCreated != 0 && msg == _wmTaskbarCreated)
        {
            RebuildLayout("taskbar_created");
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case WM_SETTINGCHANGE:
                RebuildLayout("setting_change");
                return IntPtr.Zero;

            case WM_DISPLAYCHANGE:
                RebuildLayout("display_change");
                return IntPtr.Zero;

            case WM_TIMER:
                if ((long)wParam == TIMER_RECOVER_ID) OnRecoverTimer();
                return IntPtr.Zero;

            case WM_POWERBROADCAST:
                if ((uint)wParam == PBT_APMSUSPEND)               EnterSuspendedState("sleep");
                else if ((uint)wParam == PBT_APMRESUMEAUTOMATIC)  ExitSuspendedState("wake");
                break; // let DefWindowProc return TRUE for the broadcast

            case WM_WTSSESSION_CHANGE:
                if ((uint)wParam == WTS_SESSION_LOCK)        EnterSuspendedState("lock");
                else if ((uint)wParam == WTS_SESSION_UNLOCK) ExitSuspendedState("unlock");
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // ── Cursor signal ─────────────────────────────────────────────────────

    // The single EVENT_OBJECT_LOCATIONCHANGE hook carries two signals across
    // every process: the cursor (hwnd == NULL + OBJID_CURSOR) drives the
    // reveal-zone machine; a geometry change on the *foreground* window
    // (OBJID_WINDOW, hwnd == _foregroundHwnd) is the in-place F11 toggle,
    // which raises no foreground event. Runs at input cadence — everything
    // here is a few comparisons, plus one GetCursorPos on the cursor path.
    private void OnLocationChange(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (_systemSuspended || !_layoutKnown) return;

        // The foreground window resized in place (F11): re-evaluate
        // suppression and, while visible, climb back over the taskbar.
        if (idObject == OBJID_WINDOW && idChild == 0
            && hwnd != IntPtr.Zero && hwnd == _foregroundHwnd)
        {
            EvaluateAppSuppressed();
            if (_coverVisible) ReassertTopmost();
            return;
        }

        // Cursor moves: the reveal-zone state machine.
        if (idObject != OBJID_CURSOR || hwnd != IntPtr.Zero) return;
        if (!NativeMethods.GetCursorPos(out var p)) return;

        bool inZone = CoverGeometry.Contains(_zone, p);
        if (inZone)
        {
            // Back in the zone before the re-cover delay expired: stay revealed.
            if (_recoverTimerArmed)
            {
                KillTimer(_hwnd, TIMER_RECOVER);
                _recoverTimerArmed = false;
            }
            if (!_cursorInRevealZone)
            {
                _cursorInRevealZone = true;
                UpdateCover("zone_enter");
            }
        }
        else if (_cursorInRevealZone && !_recoverTimerArmed)
        {
            // Zone exit: arm the one-shot once; the flag keeps every
            // further movement from re-arming it. On SetTimer failure the
            // flag stays false so the next movement retries — only the
            // first failure is logged, this path runs at input cadence.
            if (SetTimer(_hwnd, TIMER_RECOVER, RecoverDelayMs, IntPtr.Zero) != UIntPtr.Zero)
            {
                if (_recoverArmFailureLogged)
                    DeckleShellTaskbarCoverSource.Log.TimerArmRecovered();
                _recoverTimerArmed = true;
                _recoverArmFailureLogged = false;
            }
            else if (!_recoverArmFailureLogged)
            {
                _recoverArmFailureLogged = true;
                int error = Marshal.GetLastWin32Error(); // before any WriteEvent clobbers it
                DeckleShellTaskbarCoverSource.Log.TimerArmFailed();
                DeckleShellTaskbarCoverSource.Log.TimerArmFailedDetail("recover", error);
            }
        }
    }

    private void OnRecoverTimer()
    {
        KillTimer(_hwnd, TIMER_RECOVER); // SetTimer repeats by default
        _recoverTimerArmed = false;

        // Defensive re-check: the queue could deliver the timer after the
        // cursor came back without the hook event being processed yet —
        // never re-cover under a cursor sitting in the zone.
        if (NativeMethods.GetCursorPos(out var p) && CoverGeometry.Contains(_zone, p)) return;

        _cursorInRevealZone = false;
        UpdateCover("zone_exit_delay");
    }

    // Another app came to the foreground: track it (the location-change hook
    // compares against it to catch an in-place F11 resize), reconcile
    // suppression, and — while the band is up — climb back above the taskbar,
    // which Explorer re-asserts topmost on fullscreen exit.
    private void OnForegroundEvent(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (idObject != OBJID_WINDOW) return;
        if (_systemSuspended || !_layoutKnown) return;

        _foregroundHwnd = hwnd;
        EvaluateAppSuppressed();
        if (_coverVisible) ReassertTopmost();
    }

    // ── Geometry ──────────────────────────────────────────────────────────

    private void RebuildLayout(string reason)
    {
        var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) == IntPtr.Zero)
        {
            // No fabricated fallback: a wrong band is worse than no band.
            // The cover stays hidden until TaskbarCreated or the retry tick
            // finds the taskbar.
            if (!_layoutFailureLogged)
            {
                DeckleShellTaskbarCoverSource.Log.LayoutQueryFailed();
                _layoutFailureLogged = true;
            }
            _layoutKnown = false;
            UpdateCover("layout_unknown");
            return;
        }
        if (_layoutFailureLogged)
            DeckleShellTaskbarCoverSource.Log.LayoutQueryRecovered();
        _layoutFailureLogged = false;

        var edge = (TaskbarEdge)abd.uEdge;
        var band = abd.rc;
        bool changed = !_layoutKnown || edge != _edge
            || band.left != _band.left || band.top != _band.top
            || band.right != _band.right || band.bottom != _band.bottom;

        _layoutKnown = true;
        if (!changed) return; // WM_SETTINGCHANGE fires for plenty of unrelated settings

        _edge = edge;
        _band = band;
        _zone = CoverGeometry.RevealZone(band, edge, CoverGeometry.RevealZoneDepth);

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            band.left, band.top, band.right - band.left, band.bottom - band.top,
            NativeMethods.SWP_NOACTIVATE);

        DeckleShellTaskbarCoverSource.Log.LayoutRebuilt();
        DeckleShellTaskbarCoverSource.Log.LayoutRebuiltDetail(
            edge.ToString(), band.left, band.top, band.right, band.bottom, reason);

        // The zone moved with the band — recompute where the cursor stands.
        ResetZoneStateFromCursor();
        UpdateCover("layout_rebuilt");
    }

    // Direct re-read of the cursor against the current zone, cancelling any
    // pending re-cover delay: used at boot, on layout change and on resume,
    // where the transition logic (and its 5 s hysteresis) would be stale.
    private void ResetZoneStateFromCursor()
    {
        if (_recoverTimerArmed)
        {
            KillTimer(_hwnd, TIMER_RECOVER);
            _recoverTimerArmed = false;
        }
        _cursorInRevealZone = !_layoutKnown
            || !NativeMethods.GetCursorPos(out var p)
            || CoverGeometry.Contains(_zone, p);
    }

    // ── Gates ─────────────────────────────────────────────────────────────

    private void EnterSuspendedState(string reason)
    {
        if (_systemSuspended) return;
        _systemSuspended = true;

        if (_recoverTimerArmed)
        {
            KillTimer(_hwnd, TIMER_RECOVER);
            _recoverTimerArmed = false;
        }

        UpdateCover("suspended");
        DeckleShellTaskbarCoverSource.Log.SystemSuspended();
        DeckleShellTaskbarCoverSource.Log.SystemSuspendedDetail(reason);
    }

    private void ExitSuspendedState(string reason)
    {
        if (!_systemSuspended) return;
        _systemSuspended = false;

        ResetZoneStateFromCursor();
        _foregroundHwnd = NativeMethods.GetForegroundWindow();
        EvaluateAppSuppressed();

        UpdateCover("resumed");
        DeckleShellTaskbarCoverSource.Log.SystemResumed();
        DeckleShellTaskbarCoverSource.Log.SystemResumedDetail(reason);
    }

    // Two stages: the shell's own notion of "busy fullscreen" first (cheap,
    // catches D3D exclusive and presentation mode), then the geometric test —
    // the foreground window's DWM visual bounds covering its whole monitor
    // (catches borderless fullscreen and F11).
    private void EvaluateAppSuppressed()
    {
        bool suppressed = false;
        string stage = "none";

        if (SHQueryUserNotificationState(out int quns) == 0
            && quns is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
        {
            suppressed = true;
            stage = "notification_state";
        }

        if (!suppressed)
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && !IsDesktopWindow(fg)
                && DwmGetWindowAttribute(fg, DWMWA_EXTENDED_FRAME_BOUNDS,
                       out var bounds, (uint)Marshal.SizeOf<NativeMethods.RECT>()) == 0)
            {
                IntPtr mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
                {
                    var m = mi.rcMonitor;
                    suppressed = bounds.left <= m.left && bounds.top <= m.top
                              && bounds.right >= m.right && bounds.bottom >= m.bottom;
                    if (suppressed) stage = "fullscreen_geometry";
                }
            }
        }

        if (suppressed == _appSuppressed) return;
        _appSuppressed = suppressed;
        if (suppressed)
        {
            DeckleShellTaskbarCoverSource.Log.CoverSuppressed();
            DeckleShellTaskbarCoverSource.Log.CoverSuppressedDetail(stage);
        }
        else
        {
            DeckleShellTaskbarCoverSource.Log.CoverUnsuppressed();
        }
        UpdateCover(suppressed ? "fullscreen_enter" : "fullscreen_exit");
    }

    // The desktop covers the whole monitor and would pass the fullscreen
    // geometry test: clicking the wallpaper makes it foreground and the band
    // would stand down over a bare desktop until something else takes the
    // foreground. Excluded by the canonical guard Windows itself uses (the
    // shell and root desktop windows) plus the WorkerW host that backs an
    // animated or secondary wallpaper.
    private static bool IsDesktopWindow(IntPtr hwnd)
    {
        if (hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return true;

        var cls = new System.Text.StringBuilder(16);
        return NativeMethods.GetClassName(hwnd, cls, cls.Capacity) > 0
            && cls.ToString() == "WorkerW";
    }

    // ── Visibility — the sole ShowWindow site, idempotent ─────────────────

    private void UpdateCover(string reason)
    {
        bool shouldBeVisible = _layoutKnown && !_appSuppressed
                            && !_cursorInRevealZone && !_systemSuspended;
        if (shouldBeVisible == _coverVisible) return;

        _coverVisible = shouldBeVisible;
        if (shouldBeVisible)
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            ReassertTopmost();
            // Z-order witness: ShowWindow + the topmost assert can both succeed
            // while the band still sits below Shell_TrayWnd (the taskbar is
            // topmost too, last-positioned wins). CoverShown only proves the
            // ShowWindow call; this captures the native result — what occludes
            // the band right after the assert, and the foreground at that
            // instant — to settle the boot "covers but stays under the taskbar"
            // case the visibility log can't see.
            WindowingProbe.EmitWindowZOrderState(_hwnd, "taskbar-cover", "after_show_topmost");
            DeckleShellTaskbarCoverSource.Log.CoverShown(reason);
        }
        else
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            DeckleShellTaskbarCoverSource.Log.CoverHidden(reason);
        }
    }

    // Climb to the top of the topmost band. The taskbar is topmost too and
    // among topmost windows the most recently positioned wins, so this is how
    // the band stays above it — re-asserted when shown, on every foreground
    // change, and on the suppression poll as a fallback.
    private void ReassertTopmost() =>
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
}
