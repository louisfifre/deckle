using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Catalog;

namespace Deckle.Shell;

// ─── System notification area icon ────────────────────────────────────────────
//
// Implementation via Shell_NotifyIcon (Shell32), with no WinForms dependency.
//
// Event flow:
//   Shell32 → WM_TRAY (WM_USER+1) sent to the main HWND
//   SubclassCallback intercepte WM_TRAY
//   → left click  → OnToggleRecording invoked directly
//   → right click → RightClickRequested raised, subscriber renders the menu
//
// Context-menu responsibility lives in the sibling Deckle.Shell.TrayMenu
// module (TrayContextMenuHost), which subscribes to RightClickRequested and
// presents a native WinUI 3 MenuFlyout. This module stays Win32-pure: it does
// not know the menu contents.

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
    /// Invoked on left-click of the tray icon. Marshaling to the UI thread is
    /// the subscriber's responsibility — this handler runs on the message pump
    /// thread of the host HWND.
    /// </summary>
    public Action? OnToggleRecording { get; set; }

    /// <summary>
    /// Raised on right-click of the tray icon. The subscriber renders the
    /// context menu (typically TrayContextMenuHost from Deckle.Shell.TrayMenu).
    /// No payload: the menu host reads the current cursor position itself
    /// via GetCursorPos, since the cursor may move between WM_RBUTTONUP and
    /// the actual menu display.
    /// </summary>
    public event Action? RightClickRequested;

    // ── Initialization ───────────────────────────────────────────────────────

    public void Register(IntPtr hwnd)
    {
        _hwnd = hwnd;
        (_hIconIdle,      _iconsOwned) = LoadIconFromFile(active: false);
        (_hIconRecording, _)           = LoadIconFromFile(active: true);

        // Add the icon in the notification area.
        // Neutral placeholder: UpdateStatus("Ready", false) from App.OnLaunched
        // replaces this moments later. Keeping the string aligned with
        // UpdateStatus avoids flashing a stale "loading" message since the
        // model is lazy-loaded on first hotkey, not at boot.
        var data = BuildNotifyIconData(Loc.Format("Tray_Tooltip_Format", Loc.Get("Status_Ready")), _hIconIdle);
        bool ok = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (!ok)
            throw new InvalidOperationException(
                $"Shell_NotifyIcon(NIM_ADD) failed — hWnd={_hwnd}, hIcon={_hIconIdle}");
        _iconAdded = true;

        // Subclass the HWND to intercept WM_TRAY.
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
    // Returns the notification-area icon rect in physical pixels (screen
    // coordinates), or null if the icon could not be located (not yet
    // registered, hidden in overflow, or shell temporarily unavailable during
    // explorer.exe restart). Consumed by TrayContextMenuHost to anchor its
    // MenuFlyout tangent to the icon through CalculatePopupWindowPosition,
    // which makes positioning automatically correct regardless of taskbar
    // orientation (left, right, bottom, top) and independent of click point.
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
    // Wired in App.OnLaunched as the unique sink of TranscriptionEngine.StatusChanged:
    // every transition emitted by the engine (Loading model… → Ready →
    // Recording… → Transcribing… → Rewriting (...)… → Ready) lands here and
    // in DeckleAppSource.StatusChanged, so the tooltip stays in sync with
    // the live pipeline state visible in the LogWindow / app.jsonl.
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
                // Left click = toggle transcription (standard hotkey
                // equivalent). Allows starting/stopping with the mouse when
                // only one hand is available. Logs and Settings go through
                // right click.
                OnToggleRecording?.Invoke();
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
    // The .ico path comes from IconAssets, the source of truth shared with
    // LogWindow.
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

        // Fallback: generic Windows icon (IDI_APPLICATION = 32512). LoadIcon
        // returns a shared icon: DO NOT call DestroyIcon on it. Guarantees a
        // visible tray item even if assets are not copied.
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

        // Free only icons loaded from file (owned). System icons (LoadIcon) are
        // shared; DestroyIcon is forbidden.
        if (_iconsOwned)
        {
            if (_hIconIdle != IntPtr.Zero)      NativeMethods.DestroyIcon(_hIconIdle);
            if (_hIconRecording != IntPtr.Zero) NativeMethods.DestroyIcon(_hIconRecording);
        }
    }
}
