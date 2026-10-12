// TrayCarrierWindow — the invisible WinUI window a tray flyout anchors on.

using System;
using Deckle.Core;
using Deckle.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Deckle.Shell.TrayMenu;

// ─── Tray carrier window ──────────────────────────────────────────────────────
//
// A WinUI flyout cannot anchor to a message-only HWND or to a Shell_NotifyIcon
// icon: it needs a XamlRoot, hence a WinUI Window. This class owns that window
// and nothing else: build it invisible, finalize its style once the HWND is
// ready, place it tangent to the tray icon at a requested physical size, show
// and hide it. The flyout it carries (MenuFlyout for the context menu, Flyout
// for the quick-actions panel) belongs to the host that uses it.
//
// The window is invisible: WS_EX_LAYERED + SetLayeredWindowAttributes with
// alpha=0. The flyout renders in its own WinUI popup, so it stays fully
// visible. No WS_EX_TRANSPARENT: the window must keep focus so click-outside
// dismiss works through Activated → Deactivated.
//
// A flyout shown in Standard mode moves keyboard focus into that popup, a
// top-level window this carrier owns, and Windows then deactivates the
// carrier toward it. That is not a dismiss: a WM_ACTIVATE subclass keeps the
// window activation went to, and Deactivated is raised only when that window
// is not owned by the carrier (TrayWindowOwnership).
//
// Observability: positioning events (anchor, monitor, popup position,
// move-and-resize) go through WindowingProbe on the cross-cutting
// DeckleWindowingSource sub-provider; the carrier lifecycle (Frame.Loaded,
// style finalize, activation) goes to the module-local
// DeckleShellTrayMenuSource.

public sealed class TrayCarrierWindow : IDisposable
{
    // Owner HWND (tray message-only host). Defines the popup's parent z-order
    // so activation/deactivation chains correctly with the tray. Stored as the
    // Win32 GWLP_HWNDPARENT (-8) const offset.
    private const int GWLP_HWNDPARENT = -8;

    // Exclusion margin around the cursor, in physical pixels. Prevents the
    // popup from covering the tray icon itself (CalculatePopupWindowPosition
    // looks for a location adjacent to the exclusion rect). 36 px = typical
    // tray icon slot size at 100% DPI.
    private const int CursorExcludeHalfExtent = 18;

    private readonly IntPtr _ownerHwnd;
    private readonly string _windowName;
    private bool _loaded;
    private bool _disposed;

    // WM_ACTIVATE subclass: the delegate lives in a field so the GC keeps it
    // while native code holds its pointer (same rule as TrayIconManager).
    private readonly NativeMethods.SubclassProc _subclassProc;
    private static readonly UIntPtr SubclassId = new(0x5452_4143); // "TRAC"

    // The window activation last left for (WM_ACTIVATE lParam on WA_INACTIVE),
    // read once by the Deactivated that follows. Zero when Windows named none.
    private IntPtr _activationTarget;

    /// <summary>The carrier WinUI window. Invisible (layered alpha 0).</summary>
    public Window Window { get; }

    /// <summary>Transparent root frame; the anchor target of the flyout.</summary>
    public Frame Frame { get; }

    /// <summary>The carrier HWND.</summary>
    public IntPtr Hwnd { get; }

    /// <summary>The carrier AppWindow, used to move and resize it.</summary>
    public AppWindow AppWindow { get; }

    /// <summary>True between <see cref="ShowAt"/> and <see cref="Hide"/>.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>
    /// Raised once, from the first <c>Frame.Loaded</c>, after the window style
    /// has been finalized to <c>WS_POPUPWINDOW</c>. The host primes its flyout
    /// here: the HWND and the XamlRoot are both ready.
    /// </summary>
    public event Action? Loaded;

    /// <summary>
    /// Raised when the carrier loses activation while visible to a window it
    /// does not own (click outside, global focus loss). The host hides its
    /// flyout in response. Activation moving into the flyout's own popup
    /// window is not reported.
    /// </summary>
    public event Action? Deactivated;

    /// <param name="ownerHwnd">The tray message-only host HWND.</param>
    /// <param name="windowName">Logical name for the Windowing probe, from
    /// DeckleWindowingSource's closed <c>window</c> vocabulary.</param>
    public TrayCarrierWindow(IntPtr ownerHwnd, string windowName)
    {
        _ownerHwnd = ownerHwnd;
        _windowName = windowName;

        Window = new Window();
        Frame = new Frame
        {
            // Transparent background: the frame paints nothing, and the entire
            // window stays invisible thanks to layered alpha=0.
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        Window.Content = Frame;

        Hwnd = WindowNative.GetWindowHandle(Window);
        AppWindow = Window.AppWindow;

        // Owner = HWND of the tray message-only host. Places the popup in the
        // tray's z-order / activation stack, exactly like a native Win32
        // context menu. Without an owner, the popup would be an autonomous
        // top-level window; activation and dismiss would no longer chain
        // correctly with the tray.
        NativeMethods.SetWindowLongPtr(Hwnd, GWLP_HWNDPARENT, _ownerHwnd);

        // Win11 rounded corners: DWM clips the HWND at compositor level.
        // Applies to the whole carrier window, but because it is invisible
        // (alpha=0), the visible effect only appears on the flyout popup,
        // which follows its own rounded shape.
        uint rounded = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            Hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref rounded, sizeof(uint));

        // WS_EX_LAYERED + alpha=0: completely invisible carrier window. The
        // flyout is rendered in a separate WinUI popup (child HWND), so it
        // remains normally visible. No WS_EX_TRANSPARENT: the window must
        // receive focus for dismiss through Activated to work.
        IntPtr exStyle = NativeMethods.GetWindowLongPtr(Hwnd, NativeMethods.GWL_EXSTYLE);
        IntPtr newExStyle = new(exStyle.ToInt64() | (long)NativeMethods.WS_EX_LAYERED);
        NativeMethods.SetWindowLongPtr(Hwnd, NativeMethods.GWL_EXSTYLE, newExStyle);
        NativeMethods.SetLayeredWindowAttributes(Hwnd, 0, 0, NativeMethods.LWA_ALPHA);

        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        Window.Activated += OnWindowActivated;
        Frame.Loaded += OnFrameLoaded;

        // Installed before WinUI handles WM_ACTIVATE, so the activation
        // target is known when Window.Activated reports the deactivation.
        _subclassProc = OnSubclassMessage;
        NativeMethods.SetWindowSubclass(Hwnd, _subclassProc, SubclassId, IntPtr.Zero);
    }

    private IntPtr OnSubclassMessage(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == NativeMethods.WM_ACTIVATE
            && (wParam.ToInt64() & 0xFFFF) == NativeMethods.WA_INACTIVE)
        {
            _activationTarget = lParam;
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // ── Show ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Places the carrier tangent to the tray icon and shows it, activated.
    /// </summary>
    /// <param name="getIconRect">Tray icon screen rect accessor; null, or a
    /// null result, falls back to the cursor with a 36×36 px exclude rect.</param>
    /// <param name="measure">Returns the physical size the carrier must take,
    /// given the scale (DPI / 96) of the monitor under the anchor. The host
    /// owns the flyout, so it owns its measurement.</param>
    public void ShowAt(
        Func<NativeMethods.RECT?>? getIconRect,
        Func<double, (int Width, int Height)> measure)
    {
        if (_disposed) return;

        // Anchor + exclude: prefer the real tray icon rect
        // (Shell_NotifyIconGetRect API). This makes positioning automatically
        // correct regardless of taskbar orientation: above if the taskbar is at
        // the bottom, right if the taskbar is on the left, etc., without
        // depending on the click point on the icon. Fallback to cursor position
        // if the shell does not know (icon not yet registered, hidden in
        // overflow, or explorer.exe restarting).
        NativeMethods.RECT? iconRect = getIconRect?.Invoke();
        POINT anchor;
        NativeMethods.RECT exclude;
        int parentRectX, parentRectY, parentRectW, parentRectH;
        if (iconRect is { } icon)
        {
            anchor = new POINT { X = (icon.left + icon.right) / 2, Y = (icon.top + icon.bottom) / 2 };
            exclude = icon;
            parentRectX = icon.left;
            parentRectY = icon.top;
            parentRectW = icon.right - icon.left;
            parentRectH = icon.bottom - icon.top;
        }
        else
        {
            NativeMethods.GetCursorPos(out POINT cursor);
            anchor = cursor;
            exclude = new NativeMethods.RECT
            {
                left   = cursor.X - CursorExcludeHalfExtent,
                top    = cursor.Y - CursorExcludeHalfExtent,
                right  = cursor.X + CursorExcludeHalfExtent,
                bottom = cursor.Y + CursorExcludeHalfExtent,
            };
            // Degenerate parent rect (0×0 at the anchor point): convention
            // documented on DeckleWindowingSource.PopupAnchored for popups
            // where the app has no identified parent control.
            parentRectX = cursor.X;
            parentRectY = cursor.Y;
            parentRectW = 0;
            parentRectH = 0;
        }

        // Real DPI of the monitor under the anchor point. XamlRoot
        // RasterizationScale would reflect the DPI of the monitor where the
        // carrier window is hidden (typically primary), not where the tray
        // lives: mismatch on multi-monitor setups or a 150% primary screen.
        IntPtr monitor = TrayMenuNativeMethods.MonitorFromPoint(
            anchor, TrayMenuNativeMethods.MONITOR_DEFAULTTONEAREST);
        TrayMenuNativeMethods.GetDpiForMonitor(
            monitor, TrayMenuNativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        double scale = dpiX / 96.0;

        var (width, height) = measure(scale);
        var size = new TrayMenuNativeMethods.SIZE { cx = width, cy = height };

        NativeMethods.RECT popup = default;
        TrayMenuNativeMethods.CalculatePopupWindowPosition(
            ref anchor, ref size,
            NativeMethods.TPM_BOTTOMALIGN | TrayMenuNativeMethods.TPM_WORKAREA,
            ref exclude, ref popup);

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32
        {
            X = popup.left,
            Y = popup.top,
            Width = popup.right - popup.left,
            Height = popup.bottom - popup.top,
        });

        IsVisible = true;
        NativeMethods.ShowWindow(Hwnd, TrayMenuNativeMethods.SW_SHOWNORMAL);
        NativeMethods.SetForegroundWindow(Hwnd);

        // Canonical Windowing emission: common WindowPositioned trunk
        // (effective HWND state after MoveAndResize + ShowWindow +
        // SetForegroundWindow) and specialized PopupAnchored (parent rect =
        // tray icon or degenerate cursor rect).
        WindowingProbe.EmitWindowPositioned(Hwnd, _windowName, "CursorRelative");
        WindowingProbe.EmitPopupAnchored(
            Hwnd, _windowName,
            parentRectX, parentRectY, parentRectW, parentRectH);
    }

    // ── Hide ──────────────────────────────────────────────────────────────────

    /// <summary>Hides the carrier window. The host closes its flyout first.</summary>
    public void Hide()
    {
        IsVisible = false;
        if (Hwnd != IntPtr.Zero)
            NativeMethods.ShowWindow(Hwnd, NativeMethods.SW_HIDE);
    }

    // ── Lifecycle handlers ────────────────────────────────────────────────────

    private void OnFrameLoaded(object sender, RoutedEventArgs e)
    {
        DeckleShellTrayMenuSource.Log.FrameLoaded(_loaded);

        if (_loaded) return;
        _loaded = true;

        FinalizePopupWindow();
        Loaded?.Invoke();
    }

    // WS_POPUPWINDOW post-Loaded: clears the caption inherited from
    // OverlappedPresenter even when SetBorderAndTitleBar(false, false) was
    // called. Without this, the HWND keeps a residual WS_CAPTION visible to
    // DWM, which interferes with rounded-corner rendering.
    private void FinalizePopupWindow()
    {
        IntPtr styleBefore = NativeMethods.GetWindowLongPtr(Hwnd, NativeMethods.GWL_STYLE);
        IntPtr newStyle = new((long)TrayMenuNativeMethods.WS_POPUPWINDOW);
        NativeMethods.SetWindowLongPtr(Hwnd, NativeMethods.GWL_STYLE, newStyle);
        NativeMethods.SetWindowPos(
            Hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
        DeckleShellTrayMenuSource.Log.PrimeCycleStarted(
            styleBefore.ToInt64(), newStyle.ToInt64());
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        DeckleShellTrayMenuSource.Log.WindowActivated(
            args.WindowActivationState.ToString(), IsVisible);

        if (args.WindowActivationState != WindowActivationState.Deactivated || !IsVisible)
            return;

        IntPtr target = _activationTarget;
        _activationTarget = IntPtr.Zero;
        if (TrayWindowOwnership.IsOwnedBy(target, Hwnd, GetOwner))
        {
            DeckleShellTrayMenuSource.Log.ActivationMovedToOwnedPopup(target.ToInt64());
            return;
        }

        Deactivated?.Invoke();
    }

    private static IntPtr GetOwner(IntPtr hwnd)
        => NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Window.Activated -= OnWindowActivated;
        Frame.Loaded -= OnFrameLoaded;
        NativeMethods.RemoveWindowSubclass(Hwnd, _subclassProc, SubclassId);
        Window.Close();
    }
}
