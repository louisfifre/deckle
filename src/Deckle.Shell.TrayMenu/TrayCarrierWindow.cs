// TrayCarrierWindow — the invisible anchor window a tray menu opens from.

using System;
using Deckle.Core;
using Deckle.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Deckle.Shell.TrayMenu;

// ─── Tray anchor window ───────────────────────────────────────────────────────
//
// A WinUI MenuFlyout cannot open from a message-only HWND or from a
// Shell_NotifyIcon icon: it needs a XamlRoot, hence a WinUI Window. This class
// owns that window and nothing else: build it invisible, finalize its style
// once the HWND is ready, place it on the tray icon's rect, show and hide it.
// The menu it anchors belongs to TrayMenuHost, which sizes nothing: a
// MenuFlyout draws in a popup window of its own, at its natural size, beside
// this anchor. Only such an unconstrained flyout belongs here: a plain Flyout
// is constrained to its root by default and would draw inside this window,
// inheriting its alpha (the silent left click of v0.39.0 to v0.42.0).
//
// The window is invisible: WS_EX_LAYERED + SetLayeredWindowAttributes with
// alpha=0. No WS_EX_TRANSPARENT: the window must keep focus so click-outside
// dismiss works through Activated → Deactivated.
//
// Activation moving into the menu's own popup window is not a dismiss: a
// WM_ACTIVATE subclass keeps the window activation went to, and Deactivated is
// raised only when that window is not owned by the anchor (TrayWindowOwnership).
//
// Observability: positioning events (anchor rect, move-and-resize) go through
// WindowingProbe on the cross-cutting DeckleWindowingSource sub-provider; the
// anchor's own lifecycle (Frame.Loaded, style finalize, activation) goes to the
// module-local DeckleShellTrayMenuSource.

public sealed class TrayCarrierWindow : IDisposable
{
    // Owner HWND (tray message-only host). Defines the popup's parent z-order
    // so activation/deactivation chains correctly with the tray. Stored as the
    // Win32 GWLP_HWNDPARENT (-8) const offset.
    private const int GWLP_HWNDPARENT = -8;

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

    /// <summary>The anchor WinUI window. Invisible (layered alpha 0).</summary>
    public Window Window { get; }

    /// <summary>Transparent root frame; the element the menu opens from.</summary>
    public Frame Frame { get; }

    /// <summary>The anchor HWND.</summary>
    public IntPtr Hwnd { get; }

    /// <summary>The anchor AppWindow, used to move and resize it.</summary>
    public AppWindow AppWindow { get; }

    /// <summary>True between <see cref="ShowAt"/> and <see cref="Hide"/>.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>
    /// Raised when the anchor loses activation while visible to a window it
    /// does not own (click outside, global focus loss). The host hides its
    /// menu in response. Activation moving into the menu's own popup window
    /// is not reported.
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

        // Owner = HWND of the tray message-only host. Places the anchor in the
        // tray's z-order / activation stack, exactly like a native Win32
        // context menu. Without an owner, the anchor would be an autonomous
        // top-level window; activation and dismiss would no longer chain
        // correctly with the tray.
        NativeMethods.SetWindowLongPtr(Hwnd, GWLP_HWNDPARENT, _ownerHwnd);

        // WS_EX_LAYERED + alpha=0: completely invisible anchor window. The menu
        // is rendered in a separate WinUI popup window, so it remains normally
        // visible. No WS_EX_TRANSPARENT: the window must receive focus for
        // dismiss through Activated to work.
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
    /// Places the anchor on <paramref name="anchor"/> (physical screen pixels,
    /// the tray icon's rect) and shows it, activated. The host then opens its
    /// menu from <see cref="Frame"/>, beside the anchor.
    /// </summary>
    public void ShowAt(NativeMethods.RECT anchor)
    {
        if (_disposed) return;

        int width = Math.Max(1, anchor.right - anchor.left);
        int height = Math.Max(1, anchor.bottom - anchor.top);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32
        {
            X = anchor.left,
            Y = anchor.top,
            Width = width,
            Height = height,
        });

        IsVisible = true;
        // SW_SHOWNORMAL activates the window and SetForegroundWindow confirms
        // it (the shell grants the icon's process the foreground on a click).
        // Without activation, a click outside would not deactivate the anchor
        // and the menu would not dismiss.
        NativeMethods.ShowWindow(Hwnd, TrayMenuNativeMethods.SW_SHOWNORMAL);
        NativeMethods.SetForegroundWindow(Hwnd);

        // Canonical Windowing emission: common WindowPositioned trunk and the
        // specialized PopupAnchored (parent rect = the anchor rect: the icon,
        // or the square around the cursor when the shell does not know it).
        WindowingProbe.EmitWindowPositioned(Hwnd, _windowName, "CursorRelative");
        WindowingProbe.EmitPopupAnchored(
            Hwnd, _windowName, anchor.left, anchor.top, width, height);
    }

    // ── Hide ──────────────────────────────────────────────────────────────────

    /// <summary>Hides the anchor window. The host closes its menu first.</summary>
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
    }

    // WS_POPUPWINDOW post-Loaded: clears the caption inherited from
    // OverlappedPresenter even when SetBorderAndTitleBar(false, false) was
    // called. Without this, the HWND keeps a residual WS_CAPTION, and a
    // captioned window carries a system minimum size that would make the
    // anchor larger than the icon it stands on.
    private void FinalizePopupWindow()
    {
        IntPtr styleBefore = NativeMethods.GetWindowLongPtr(Hwnd, NativeMethods.GWL_STYLE);
        IntPtr newStyle = new((long)TrayMenuNativeMethods.WS_POPUPWINDOW);
        NativeMethods.SetWindowLongPtr(Hwnd, NativeMethods.GWL_STYLE, newStyle);
        NativeMethods.SetWindowPos(
            Hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
        DeckleShellTrayMenuSource.Log.CarrierStyleFinalized(
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
