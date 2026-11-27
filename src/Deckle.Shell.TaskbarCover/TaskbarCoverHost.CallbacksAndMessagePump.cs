using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // First layout off Start()'s critical path: the gated section
    // (SetupWindow) makes local calls only, and the layout walks the taskbar
    // windows, places each band, reads its level from the cursor and applies
    // the foreground verdict in one pass.
    private void BootstrapState()
    {
        RebuildLayout("boot");
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
        if (_cloakHook != IntPtr.Zero)
        {
            UnhookWinEvent(_cloakHook);
            _cloakHook = IntPtr.Zero;
        }
        RestoreAll("host_stopped");
        StopUnderlayMotion();
        foreach (var band in _bands) DisposeUnderlay(band);
        _bands.Clear();
        _fadedTaskbars = [];
        if (_hwnd != IntPtr.Zero)
        {
            WTSUnRegisterSessionNotification(_hwnd);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        if (_classAtom != 0)
        {
            NativeMethods.UnregisterClass(ClassName, _hInstance);
            _classAtom = 0;
        }

        // Restore the boot-pessimistic state for a potential restart; the
        // per-band state left with the bands.
        _systemSuspended     = false;
        _layoutKnown         = false;
        _layoutFailureLogged = false;
        _fadeFailureLogged   = false;
        _underlayFailureLogged = false;
        _underlaySoonPending = false;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // _hwnd is still zero while the anchor itself is being created, and
        // its creation messages take the default path like any other.
        if (hWnd != _hwnd)
        {
            if (msg == WM_ERASEBKGND && TryPaintUnderlay(hWnd, wParam)) return 1;
            // A strip: Windows hides an owned window itself when its owner
            // is minimized or another window is maximized (lParam names
            // why; zero is a ShowWindow call of Deckle's own). Refused, so
            // that the strip only ever leaves through its own fade.
            if (msg == WM_SHOWWINDOW && lParam != IntPtr.Zero) return IntPtr.Zero;
            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        // Explorer's restart re-creates the taskbar: the old window took its
        // alpha with it, and the rebuild fades the new one (a new handle is a
        // new placement).
        if (_wmTaskbarCreated != 0 && msg == _wmTaskbarCreated)
        {
            RebuildLayout("taskbar_created");
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case WM_APP_FOREGROUND:
                // Posted by OnForegroundChanged; the snapshot is read here,
                // so a burst of posts converges on the latest one.
                ApplyForeground(_foreground.Current);
                return IntPtr.Zero;

            case WM_APP_APPROACH:
                // Posted by the ApproachDistanceDip setter and by Start; the
                // distance is read here, so a burst of posts converges on the
                // latest one and a post that finds the zones already at that
                // distance costs nothing. The zones are rebuilt through the
                // layout path, the one place that computes them.
                if (_requestedApproachDip != _approachDip) RebuildLayout("approach_change");
                return IntPtr.Zero;

            case WM_APP_UNDERLAY_FRAME:
                OnUnderlayFrame();
                return IntPtr.Zero;

            case WM_APP_UNDERLAY_EDGE:
                OnUnderlayEdge(wParam, lParam);
                return IntPtr.Zero;

            case WM_TIMER:
                OnUnderlayTimer((UIntPtr)(ulong)wParam);
                return IntPtr.Zero;

            case WM_SETTINGCHANGE:
                // "ImmersiveColorSet": the light or dark theme changed.
                if (lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet") OnThemeChanged();
                RebuildLayout("setting_change");
                return IntPtr.Zero;

            case WM_DISPLAYCHANGE:
                RebuildLayout("display_change");
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
}
