using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Catalog;

namespace Deckle.Shell;

// The notification-area icon, through Shell_NotifyIcon (Shell32) with no
// WinForms dependency.
//
// Both surfaces live in the sibling Deckle.Shell.TrayMenu module: the
// quick-actions panel (TrayQuickActionsHost) on left click, the context menu
// (TrayContextMenuHost) on right click. This module stays Win32-pure: it does
// not know what either surface contains.
//
// A double click is delivered by the shell as WM_LBUTTONUP, WM_LBUTTONDBLCLK,
// WM_LBUTTONUP: the single-click subscriber runs first, then the double-click
// one, then a trailing left click. Each subscriber copes with that order
// itself; the manager does not delay the single click to tell them apart.
public sealed class TrayIconManager : IDisposable
{
    private IntPtr _hwnd;
    private IntPtr _hIconIdle;
    private IntPtr _hIconRecording;
    private bool   _iconsOwned;   // false if fallback LoadIcon (shared icon: no DestroyIcon)
    private bool   _iconAdded;
    private bool   _disposed;

    // SubclassProc delegate: must live in a field to avoid GC collection.
    private NativeMethods.SubclassProc? _subclassDelegate;
    private static readonly UIntPtr SubclassId = new(0x5752_4159); // "WRAY"

    /// <summary>
    /// Raised on left-click of the tray icon, on the message pump thread of
    /// the host HWND; marshaling elsewhere is the subscriber's responsibility.
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

    // ── Initialization ───────────────────────────────────────────────────────

    public void Register(IntPtr hwnd)
    {
        _hwnd = hwnd;
        (_hIconIdle,      _iconsOwned) = LoadIconFromFile(active: false);
        (_hIconRecording, _)           = LoadIconFromFile(active: true);

        var data = BuildNotifyIconData(Loc.Format("Tray_Tooltip_Format", Loc.Get("Status_Ready")), _hIconIdle);
        bool ok = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (!ok)
            throw new InvalidOperationException(
                $"Shell_NotifyIcon(NIM_ADD) failed — hWnd={_hwnd}, hIcon={_hIconIdle}");
        _iconAdded = true;

        _subclassDelegate = SubclassCallback;
        if (!NativeMethods.SetWindowSubclass(
                _hwnd, _subclassDelegate, SubclassId, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            Dispose();
            throw new InvalidOperationException(
                $"SetWindowSubclass failed for the tray icon (Win32 err {error}).");
        }
    }

    // ── Position rect ─────────────────────────────────────────────────────────
    //
    // The icon rect in physical pixels (screen coordinates), or null if the
    // icon could not be located (not yet registered, hidden in overflow, or
    // the shell unavailable during an explorer.exe restart). The tray surfaces
    // anchor on it through CalculatePopupWindowPosition, which works for any
    // taskbar orientation and any click point.
    public NativeMethods.RECT? GetIconRect()
    {
        if (!_iconAdded) return null;

        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = 1,
            guidItem = Guid.Empty,
        };

        int hr = NativeMethods.Shell_NotifyIconGetRect(ref id, out NativeMethods.RECT rect);
        return hr >= 0 ? rect : null;
    }

    // ── Status update ───────────────────────────────────────────────────────
    //
    // Tip caps at 127 chars (Shell_NotifyIcon szTip limit). The host supplies
    // the semantic state separately: this shell primitive never interprets
    // the localized status text.
    public void UpdateStatus(string status, bool isRecording)
    {
        IntPtr icon = isRecording ? _hIconRecording : _hIconIdle;

        string tip = Loc.Format("Tray_Tooltip_Format", status);
        if (tip.Length > 127) tip = tip[..127];

        var data = BuildNotifyIconData(tip, icon);
        data.uFlags = NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    // ── Tray message interception ────────────────────────────────────────────

    private IntPtr SubclassCallback(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == NativeMethods.WM_TRAY)
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
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private NOTIFYICONDATA BuildNotifyIconData(string tip, IntPtr hIcon)
    {
        return new NOTIFYICONDATA
        {
            cbSize          = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd            = _hwnd,
            uID             = 1,
            uFlags          = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = NativeMethods.WM_TRAY,
            hIcon           = hIcon,
            szTip           = tip,
            szInfo          = "",
            szInfoTitle     = "",
        };
    }

    // Returns (hIcon, owned): owned=true if loaded from file (DestroyIcon
    // required), owned=false if shared system icon (DO NOT call DestroyIcon).
    private static (IntPtr hIcon, bool owned) LoadIconFromFile(bool active)
    {
        string? path = IconAssets.ResolvePath(recording: active);
        if (path is not null)
        {
            IntPtr hIcon = NativeMethods.LoadImage(
                IntPtr.Zero, path,
                NativeMethods.IMAGE_ICON, 32, 32,
                NativeMethods.LR_LOADFROMFILE);

            if (hIcon != IntPtr.Zero)
                return (hIcon, owned: true);
        }

        // The shared IDI_APPLICATION icon keeps the tray item visible when the
        // assets were not copied.
        const nint IDI_APPLICATION = 32512;
        return (NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(IDI_APPLICATION)), owned: false);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_subclassDelegate is not null && _hwnd != IntPtr.Zero)
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId);

        if (_iconAdded)
        {
            var data = BuildNotifyIconData("", IntPtr.Zero);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        }

        if (_iconsOwned)
        {
            if (_hIconIdle != IntPtr.Zero)      NativeMethods.DestroyIcon(_hIconIdle);
            if (_hIconRecording != IntPtr.Zero) NativeMethods.DestroyIcon(_hIconRecording);
        }
    }
}
