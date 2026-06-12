using System.Runtime.InteropServices;
using Deckle.Core.Interop;
using Deckle.Shell.TaskbarCover.Interop;
using static Deckle.Shell.TaskbarCover.Interop.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

// Dedicated thread owning the cover band — an opaque black topmost window
// sized to the exact taskbar rect — and the whole reveal state machine:
//
//   • the band covers the taskbar whatever edge it is anchored to
//     (ABM_GETTASKBARPOS rect, rebuilt on WM_SETTINGCHANGE /
//     WM_DISPLAYCHANGE / TaskbarCreated);
//   • the cursor entering the reveal zone hides the band instantly;
//     leaving it re-covers after RecoverDelayMs;
//   • a fullscreen / presentation foreground app suppresses the band
//     (5 s poll — F11 fullscreen changes no foreground window, so a
//     foreground event alone would miss it);
//   • sleep and session lock park the machine entirely.
//
// Cursor movement arrives through a WinEvent hook
// (EVENT_OBJECT_LOCATIONCHANGE filtered on OBJID_CURSOR), not Raw Input:
// the RIDEV_INPUTSINK mouse registration is per-process-per-usage and
// HudWindow owns the only one (proximity fade). The hook is asynchronous
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

    // Fullscreen-suppression poll cadence — latency is acceptable for the
    // fullscreen transition, overhead minimal. Doubles as the topmost
    // re-assertion tick while the band is visible.
    private const uint SuppressionPollMs = 5000;

    private const uint TIMER_RECOVER_ID     = 1;
    private const uint TIMER_SUPPRESSION_ID = 2;
    private static readonly UIntPtr TIMER_RECOVER     = new(TIMER_RECOVER_ID);
    private static readonly UIntPtr TIMER_SUPPRESSION = new(TIMER_SUPPRESSION_ID);

    private readonly object _stateLock = new();

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hwnd;
    private ushort _classAtom;
    private IntPtr _hInstance;
    private IntPtr _brush;
    private IntPtr _cursorHook;
    private uint _wmTaskbarCreated;

    // Rooted for the GC while native code holds their function pointers.
    private NativeMethods.WndProc? _wndProcDelegate;
    private WinEventProc? _cursorHookDelegate;

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

    private bool _layoutKnown;
    private bool _layoutFailureLogged;
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
                DeckleShellTaskbarCoverSource.Log.HostStartFailed(
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
        lock (_stateLock)
        {
            if (!_running || _thread is null) return;
            _running = false;
            thread = _thread;
            _thread = null;
        }

        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(3));
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
        _hwnd = NativeMethods.CreateWindowEx(
            dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
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
        _cursorHookDelegate = OnCursorEvent;
        _cursorHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _cursorHookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_cursorHook == IntPtr.Zero)
        {
            DeckleShellTaskbarCoverSource.Log.CursorHookFailed();
            throw new InvalidOperationException("SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE) failed");
        }

        if (!WTSRegisterSessionNotification(_hwnd, NOTIFY_FOR_THIS_SESSION))
            DeckleShellTaskbarCoverSource.Log.SessionNotifyFailed();

        RebuildLayout("boot");
        ResetZoneStateFromCursor();
        EvaluateAppSuppressed();

        SetTimer(_hwnd, TIMER_SUPPRESSION, SuppressionPollMs, IntPtr.Zero);

        UpdateCover("boot");
        DeckleShellTaskbarCoverSource.Log.HostStarted(_hwnd.ToInt64(), (int)_threadId);
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
        if (_hwnd != IntPtr.Zero)
        {
            KillTimer(_hwnd, TIMER_SUPPRESSION);
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
        _layoutKnown        = false;
        _layoutFailureLogged = false;
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
                else if ((long)wParam == TIMER_SUPPRESSION_ID) OnSuppressionTimer();
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

    private void OnCursorEvent(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        // LOCATIONCHANGE also fires for windows and carets across every
        // process — the cursor signature is hwnd == NULL + OBJID_CURSOR.
        // This callback runs at input cadence; everything below is a few
        // comparisons and one GetCursorPos.
        if (idObject != OBJID_CURSOR || hwnd != IntPtr.Zero) return;
        if (_systemSuspended || !_layoutKnown) return;
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
            // further movement from re-arming it.
            _recoverTimerArmed = true;
            SetTimer(_hwnd, TIMER_RECOVER, RecoverDelayMs, IntPtr.Zero);
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

    private void OnSuppressionTimer()
    {
        // Retry path for a shell that was not ready at boot — TaskbarCreated
        // covers the normal case, this tick the degenerate ones.
        if (!_layoutKnown) RebuildLayout("retry");

        EvaluateAppSuppressed();

        // The taskbar is topmost too; among topmost windows the most
        // recently positioned wins. Re-asserting on this slow tick keeps
        // the band above it whatever the shell re-ordered meanwhile.
        if (_coverVisible)
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
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

        DeckleShellTaskbarCoverSource.Log.LayoutRebuilt(
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

        KillTimer(_hwnd, TIMER_SUPPRESSION);
        if (_recoverTimerArmed)
        {
            KillTimer(_hwnd, TIMER_RECOVER);
            _recoverTimerArmed = false;
        }

        UpdateCover("suspended");
        DeckleShellTaskbarCoverSource.Log.SystemSuspended(reason);
    }

    private void ExitSuspendedState(string reason)
    {
        if (!_systemSuspended) return;
        _systemSuspended = false;

        ResetZoneStateFromCursor();
        EvaluateAppSuppressed();
        SetTimer(_hwnd, TIMER_SUPPRESSION, SuppressionPollMs, IntPtr.Zero);

        UpdateCover("resumed");
        DeckleShellTaskbarCoverSource.Log.SystemResumed(reason);
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
            if (fg != IntPtr.Zero
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
        DeckleShellTaskbarCoverSource.Log.SuppressionChanged(suppressed, stage);
        UpdateCover(suppressed ? "fullscreen_enter" : "fullscreen_exit");
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
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            DeckleShellTaskbarCoverSource.Log.CoverShown(reason);
        }
        else
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            DeckleShellTaskbarCoverSource.Log.CoverHidden(reason);
        }
    }
}
