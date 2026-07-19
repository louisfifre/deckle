using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
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

}
