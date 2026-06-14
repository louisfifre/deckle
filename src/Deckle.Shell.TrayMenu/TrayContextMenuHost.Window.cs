// TrayContextMenuHost — carrier window construction, finalize, and activation.

using System;
using System.Diagnostics;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TrayMenu;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Deckle.Shell.TrayMenu;

public sealed partial class TrayContextMenuHost
{
    // ── Build window ──────────────────────────────────────────────────────────

    private void BuildWindow()
    {
        _window = new Window();
        _frame = new Frame
        {
            // Transparent background: the frame paints nothing, and the entire
            // window stays invisible thanks to layered alpha=0.
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        _window.Content = _frame;

        _hwnd = WindowNative.GetWindowHandle(_window);
        _appWindow = _window.AppWindow;

        // Owner = HWND of the tray message-only host. Places the popup in the
        // tray's z-order / activation stack, exactly like a native Win32
        // context menu. Without an owner, the popup would be an autonomous
        // top-level window; activation and dismiss would no longer chain
        // correctly with the tray.
        NativeMethods.SetWindowLongPtr(_hwnd, GWLP_HWNDPARENT, _ownerHwnd);

        // Win11 rounded corners: DWM clips the HWND at compositor level.
        // Applies to the whole carrier window, but because it is invisible
        // (alpha=0), the visible effect only appears on the MenuFlyout popup,
        // which follows its own rounded shape.
        uint rounded = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref rounded, sizeof(uint));

        // WS_EX_LAYERED + alpha=0: completely invisible carrier window. The
        // MenuFlyout is rendered in a separate WinUI popup (child HWND), so it
        // remains normally visible. No WS_EX_TRANSPARENT: the window must
        // receive focus for dismiss through Activated to work.
        IntPtr exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        IntPtr newExStyle = new(exStyle.ToInt64() | (long)NativeMethods.WS_EX_LAYERED);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, newExStyle);
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0, NativeMethods.LWA_ALPHA);

        if (_appWindow is not null)
        {
            _appWindow.IsShownInSwitchers = false;
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
                presenter.IsAlwaysOnTop = true;
                presenter.SetBorderAndTitleBar(false, false);
            }
        }

        _window.Activated += OnWindowActivated;
        _frame.Loaded += OnFrameLoaded;
    }

    // ── Window finalize (WS_POPUPWINDOW) ──────────────────────────────────────

    // WS_POPUPWINDOW post-Loaded: clears the caption inherited from
    // OverlappedPresenter even when SetBorderAndTitleBar(false, false) was
    // called. Without this, the HWND keeps a residual WS_CAPTION visible to
    // DWM, which interferes with rounded-corner rendering.
    private void FinalizePopupWindow()
    {
        IntPtr styleBefore = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE);
        IntPtr newStyle = new((long)TrayMenuNativeMethods.WS_POPUPWINDOW);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_STYLE, newStyle);
        NativeMethods.SetWindowPos(
            _hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED);
        DeckleShellTrayMenuSource.Log.PrimeCycleStarted(
            styleBefore.ToInt64(), newStyle.ToInt64());
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        DeckleShellTrayMenuSource.Log.WindowActivated(
            args.WindowActivationState.ToString(), _isVisible);

        if (args.WindowActivationState == WindowActivationState.Deactivated && _isVisible)
            Hide("deactivated");
    }
}
