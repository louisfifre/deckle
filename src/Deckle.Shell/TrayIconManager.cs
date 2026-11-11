using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Localization;

namespace Deckle.Shell;

// The notification-area icon, through Shell_NotifyIcon (Shell32) with no
// WinForms dependency.
//
// Both menus live in the sibling Deckle.Shell.TrayMenu module as two
// TrayMenuHost instances the App composes. This module stays Win32-pure: it
// does not know what either menu contains.
//
// The icon's owner is a hidden top-level window of its own, created here on
// the thread that calls Register (the UI thread, so every tray callback
// arrives on the DispatcherQueue's thread). Not the message-only host: a
// message-only window receives no broadcast, and the one broadcast this
// icon lives by is TaskbarCreated, which Explorer sends to every top-level
// window when it rebuilds the taskbar. The shell starts that taskbar with an
// empty notification area, so the icon has to be added again, with the icon
// and tip of the last UpdateStatus. Explorer restarts happen (Windhawk, a
// crash, a manual restart), and without the re-add the icon stays lost.
//
// A double click is delivered by the shell as WM_LBUTTONUP, WM_LBUTTONDBLCLK,
// WM_LBUTTONUP: the single-click subscriber runs first, then the double-click
// one, then a trailing left click. Each subscriber copes with that order
// itself; the manager does not delay the single click to tell them apart.
public sealed class TrayIconManager : IDisposable
{
    private const string ClassName = "DeckleTrayIconHost";

    // One re-add attempt every half second while the shell refuses it, up to
    // TrayIconRegistration.MaxReaddAttempts.
    private const uint ReaddRetryDelayMs = 500;
    private static readonly UIntPtr ReaddTimerId = new(1);

    private IntPtr _hInstance;
    private ushort _classAtom;
    private IntPtr _hwnd;
    private uint   _wmTaskbarCreated;

    // The six marks, indexed by IconIndex: each MarkState with and without
    // the dot of an open user incident. Owned is false for the fallback
    // LoadIcon, a shared icon that DestroyIcon must not touch.
    private readonly (IntPtr Icon, bool Owned)[] _icons = new (IntPtr, bool)[6];
    private bool   _disposed;

    private TrayIconRegistration? _registration;

    // The WndProc delegate must live in a field for the GC not to collect it
    // while native code holds its function pointer.
    private NativeMethods.WndProc? _wndProcDelegate;

    // Status kept before Register, so the add carries it: the App sets the
    // initial status before the icon goes live. The recording state arrives
    // on the engine's threads and the incident dot on the UI thread; the gate
    // keeps each update's tip and icon from mixing two callers' halves.
    private readonly object _statusGate = new();
    private string _tip = "";
    private bool   _isRecording;
    private bool   _isRewriting;
    private bool   _hasIncident;

    /// <summary>
    /// Raised on left-click of the tray icon, on the message pump thread of
    /// the owner window; marshaling elsewhere is the subscriber's responsibility.
    /// </summary>
    public event Action? LeftClickRequested;

    /// <summary>
    /// Raised on a left double-click of the tray icon, after the first
    /// click's <see cref="LeftClickRequested"/> and before a trailing one
    /// (see the class header). Raised on the message pump thread.
    /// </summary>
    public event Action? DoubleClickRequested;

    /// <summary>
    /// Raised on right-click of the tray icon. No payload: the menu host
    /// anchors on <see cref="GetIconRect"/>, with the cursor as its fallback.
    /// </summary>
    public event Action? RightClickRequested;

    /// <summary>
    /// Raised each time Explorer rebuilds the taskbar (TaskbarCreated), once
    /// the icon's re-add has been tried, on the message pump thread of the
    /// owner window. The re-add itself may still be retrying on its timer.
    /// A subscriber must not let an exception out: it would unwind into the
    /// WndProc.
    /// </summary>
    public event Action? TaskbarCreated;

    // ── Initialization ───────────────────────────────────────────────────────

    // Creates the owner window and adds the icon with the current status.
    // Throws only when the window cannot be created. A shell that refuses the
    // first add is retried on the re-add timer, and the window stays alive to
    // hear TaskbarCreated: the app goes on starting, hotkeys included, and the
    // icon appears as soon as the shell takes it. Throwing there once cost the
    // icon and every hotkey for the whole run (2026-11-11), with no window
    // left to hear Explorer come back.
    public void Register()
    {
        foreach (MarkState state in Enum.GetValues<MarkState>())
        foreach (bool incident in new[] { false, true })
            _icons[IconIndex(state, incident)] = LoadIconFromFile(state, incident);

        CreateOwnerWindow();

        lock (_statusGate)
        {
            _registration = new TrayIconRegistration(_hwnd, new Win32NotifyIconApi());
            _registration.Update(_tip, CurrentIcon());
        }
        if (_registration.Add() == TrayIconReaddOutcome.Added) return;

        DeckleShellSource.Log.TrayIconAddRefused(Marshal.GetLastWin32Error());
        NativeMethods.SetTimer(_hwnd, ReaddTimerId, ReaddRetryDelayMs, IntPtr.Zero);
    }

    private static int IconIndex(MarkState state, bool incident)
        => (int)state + (incident ? 3 : 0);

    // An open microphone is the signal that must not be missed, so it wins
    // over a rewrite that overlaps it.
    internal static MarkState MarkFor(bool recording, bool rewriting)
        => recording ? MarkState.Recording
         : rewriting ? MarkState.Rewriting
         : MarkState.Rest;

    // Under _statusGate.
    private IntPtr CurrentIcon()
        => _icons[IconIndex(MarkFor(_isRecording, _isRewriting), _hasIncident)].Icon;

    // ── Owner window ─────────────────────────────────────────────────────────
    //
    // WS_POPUP, never shown: no z-order on screen, no flash, no taskbar
    // button. Top-level, so Explorer's broadcasts reach its WndProc.

    private void CreateOwnerWindow()
    {
        _hInstance = NativeMethods.GetModuleHandle(null);
        if (_hInstance == IntPtr.Zero)
            throw new InvalidOperationException(
                $"GetModuleHandle failed (Win32 err {Marshal.GetLastWin32Error()})");

        _wmTaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _wndProcDelegate = WndProc;

        var wc = new WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance     = _hInstance,
            lpszClassName = ClassName,
        };

        _classAtom = NativeMethods.RegisterClassEx(ref wc);
        if (_classAtom == 0)
            throw new InvalidOperationException(
                $"RegisterClassEx('{ClassName}') failed (Win32 err {Marshal.GetLastWin32Error()})");

        _hwnd = NativeMethods.CreateWindowEx(
            dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW,
            lpClassName:  ClassName,
            lpWindowName: null,
            dwStyle:      NativeMethods.WS_POPUP,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent:   IntPtr.Zero,
            hMenu:        IntPtr.Zero,
            hInstance:    _hInstance,
            lpParam:      IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            NativeMethods.UnregisterClass(ClassName, _hInstance);
            _classAtom = 0;
            throw new InvalidOperationException(
                $"CreateWindowEx('{ClassName}') failed (Win32 err {error})");
        }

        DeckleShellSource.Log.TrayIconHostCreated(_hwnd.ToInt64(), _wmTaskbarCreated);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_TRAY)
        {
            uint mouseEvent = (uint)(lParam.ToInt64() & 0xFFFF);

            if (mouseEvent == NativeMethods.WM_RBUTTONUP)
            {
                RightClickRequested?.Invoke();
                return IntPtr.Zero;
            }

            if (mouseEvent == NativeMethods.WM_LBUTTONUP)
            {
                LeftClickRequested?.Invoke();
                return IntPtr.Zero;
            }

            if (mouseEvent == NativeMethods.WM_LBUTTONDBLCLK)
            {
                DoubleClickRequested?.Invoke();
                return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        if (_wmTaskbarCreated != 0 && msg == _wmTaskbarCreated)
        {
            DeckleShellSource.Log.TaskbarRecreated();
            TryReadd();
            TaskbarCreated?.Invoke();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_TIMER && unchecked((nuint)(nint)wParam) == ReaddTimerId)
        {
            TryReadd();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // ── Re-add after an Explorer restart ─────────────────────────────────────
    //
    // Runs on the owner window's thread, once per TaskbarCreated and once per
    // timer tick. A refused add is tried again after a pause, never by
    // sleeping this thread: the timer puts the next attempt in the pump.

    private void TryReadd()
    {
        if (_registration is null || _disposed) return;

        TrayIconReaddOutcome outcome = _registration.Readd();
        int error = outcome == TrayIconReaddOutcome.Added ? 0 : Marshal.GetLastWin32Error();
        DeckleShellSource.Log.TrayIconReaddAttempt(
            _registration.ReaddAttempts, TrayIconRegistration.MaxReaddAttempts, outcome.ToString(), error);

        switch (outcome)
        {
            case TrayIconReaddOutcome.Added:
                NativeMethods.KillTimer(_hwnd, ReaddTimerId);
                DeckleShellSource.Log.TrayIconRestored();
                break;

            case TrayIconReaddOutcome.RetryLater:
                NativeMethods.SetTimer(_hwnd, ReaddTimerId, ReaddRetryDelayMs, IntPtr.Zero);
                break;

            case TrayIconReaddOutcome.GaveUp:
                NativeMethods.KillTimer(_hwnd, ReaddTimerId);
                DeckleShellSource.Log.TrayIconRestoreFailed(_registration.ReaddAttempts, error);
                break;
        }
    }

    // ── Position rect ─────────────────────────────────────────────────────────
    //
    // The icon rect in physical pixels (screen coordinates), or null if the
    // icon could not be located (not yet registered, hidden in overflow, or
    // the shell unavailable during an explorer.exe restart). TrayMenuHost puts
    // its anchor window on it, so the menu opens beside the icon on the side
    // facing the work area, whatever the taskbar edge and the click point.
    public NativeMethods.RECT? GetIconRect()
    {
        if (_registration is not { IsAdded: true }) return null;

        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = TrayIconRegistration.IconId,
            guidItem = Guid.Empty,
        };

        int hr = NativeMethods.Shell_NotifyIconGetRect(ref id, out NativeMethods.RECT rect);
        return hr >= 0 ? rect : null;
    }

    // ── Status update ───────────────────────────────────────────────────────
    //
    // The host supplies the semantic state separately: this shell primitive
    // never interprets the localized status text. Before Register the status
    // is only kept; the add then carries it.
    public void UpdateStatus(string status, bool isRecording)
    {
        lock (_statusGate)
        {
            _tip = Loc.Format("Tray_Tooltip_Format", status);
            _isRecording = isRecording;
            _registration?.Update(_tip, CurrentIcon());
        }
    }

    // Lights or dims the blade that says a rewrite is running. The host names
    // the phase; the tooltip keeps following UpdateStatus.
    public void SetRewritingIndicator(bool shown)
    {
        lock (_statusGate)
        {
            if (_isRewriting == shown) return;
            _isRewriting = shown;
            _registration?.Update(_tip, CurrentIcon());
        }
    }

    // Shows or hides the dot that says a user incident is open. The host
    // decides what counts; this primitive only swaps the mark, in every
    // state.
    public void SetIncidentIndicator(bool shown)
    {
        lock (_statusGate)
        {
            if (_hasIncident == shown) return;
            _hasIncident = shown;
            _registration?.Update(_tip, CurrentIcon());
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Returns (hIcon, owned): owned=true if loaded from file (DestroyIcon
    // required), owned=false if shared system icon (DO NOT call DestroyIcon).
    internal static (IntPtr hIcon, bool owned) LoadIconFromFile(MarkState mark, bool incident)
    {
        string? path = IconAssets.ResolveTrayIconPath(mark, incident);
        string reason = "asset_missing";
        int error = 0;
        if (path is not null)
        {
            IntPtr hIcon = NativeMethods.LoadImage(
                IntPtr.Zero, path,
                NativeMethods.IMAGE_ICON, 32, 32,
                NativeMethods.LR_LOADFROMFILE);

            if (hIcon != IntPtr.Zero)
                return (hIcon, owned: true);

            reason = "load_failed";
            error = Marshal.GetLastWin32Error();
        }

        // The shared IDI_APPLICATION icon keeps the tray item visible when the
        // assets were not copied; the tray then shows the wrong mark, so the
        // fallback is a Warning, not a silence.
        string state = mark switch
        {
            MarkState.Recording => "recording",
            MarkState.Rewriting => "rewriting",
            _                   => "idle",
        } + (incident ? "_incident" : "");
        DeckleShellSource.Log.TrayIconFallback(state, reason, error);
        const nint IDI_APPLICATION = 32512;
        return (NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(IDI_APPLICATION)), owned: false);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
            NativeMethods.KillTimer(_hwnd, ReaddTimerId);

        _registration?.Delete();

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            DeckleShellSource.Log.TrayIconHostDestroyed(_hwnd.ToInt64());
            _hwnd = IntPtr.Zero;
        }

        if (_classAtom != 0)
        {
            NativeMethods.UnregisterClass(ClassName, _hInstance);
            _classAtom = 0;
        }

        foreach ((IntPtr icon, bool owned) in _icons)
        {
            if (owned && icon != IntPtr.Zero) NativeMethods.DestroyIcon(icon);
        }
    }
}
