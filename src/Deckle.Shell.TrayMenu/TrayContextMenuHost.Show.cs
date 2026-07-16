// TrayContextMenuHost — anchor, DPI, popup placement, show and hide.

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
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
    // ── Show ──────────────────────────────────────────────────────────────────

    public void Show()
    {
        if (_disposed || _window is null || _frame is null || _flyout is null || _appWindow is null)
            return;

        bool traceWindowing = DeckleShellTrayMenuSource.IsDetailEnabled(
            EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle);
        if (traceWindowing)
        {
            long nowTickMs = Environment.TickCount64;
            double msSinceLastShow = _showCount == 0 ? 0 : (nowTickMs - _lastShowTickMs);
            _showCount++;
            _lastShowTickMs = nowTickMs;
            DeckleShellTrayMenuSource.Log.ShowRequested();
            DeckleShellTrayMenuSource.Log.ShowRequestedDetail(msSinceLastShow, _showCount);
        }

        if (_ambientItem is not null && IsAmbientOn is not null)
        {
            bool ambientOn = IsAmbientOn();
            TraySwitchMenuItem.SetState(_ambientItem, ambientOn);
            if (traceWindowing) DeckleShellTrayMenuSource.Log.AmbientStateRead(ambientOn);
        }

        if (_taskbarCoverItem is not null && IsTaskbarCoverOn is not null)
        {
            bool coverOn = IsTaskbarCoverOn();
            TraySwitchMenuItem.SetState(_taskbarCoverItem, coverOn);
            if (traceWindowing) DeckleShellTrayMenuSource.Log.TaskbarCoverStateRead(coverOn);
        }

        // Anchor + exclude: prefer the real tray icon rect
        // (Shell_NotifyIconGetRect API). This makes positioning automatically
        // correct regardless of taskbar orientation: above if the taskbar is at
        // the bottom, right if the taskbar is on the left, etc., without
        // depending on the click point on the icon. Fallback to cursor position
        // if the shell does not know (icon not yet registered, hidden in
        // overflow, or explorer.exe restarting).
        NativeMethods.RECT? iconRect = GetIconRect?.Invoke();
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

        var (width, height) = MeasureFlyout(scale);
        var size = new TrayMenuNativeMethods.SIZE { cx = width, cy = height };

        NativeMethods.RECT popup = default;
        TrayMenuNativeMethods.CalculatePopupWindowPosition(
            ref anchor, ref size,
            NativeMethods.TPM_BOTTOMALIGN | TrayMenuNativeMethods.TPM_WORKAREA,
            ref exclude, ref popup);

        _appWindow.MoveAndResize(new Windows.Graphics.RectInt32
        {
            X = popup.left,
            Y = popup.top,
            Width = popup.right - popup.left,
            Height = popup.bottom - popup.top,
        });

        _isVisible = true;
        NativeMethods.ShowWindow(_hwnd, TrayMenuNativeMethods.SW_SHOWNORMAL);
        NativeMethods.SetForegroundWindow(_hwnd);

        // Canonical Windowing emission: common WindowPositioned trunk
        // (effective HWND state after MoveAndResize + ShowWindow +
        // SetForegroundWindow) and specialized PopupAnchored (parent rect =
        // tray icon or degenerate cursor rect).
        WindowingProbe.EmitWindowPositioned(_hwnd, "tray-popup", "CursorRelative");
        WindowingProbe.EmitPopupAnchored(
            _hwnd, "tray-popup",
            parentRectX, parentRectY, parentRectW, parentRectH);

        // FlyoutPlacementMode.Full: opens the menu at the exact target
        // location (our frame). Without Full, default Top placement places the
        // popup above the frame, adding a vertical offset on top of the one
        // already calculated by CalculatePopupWindowPosition; the menu jumps up
        // by roughly one menu height. Since the carrier window is already
        // positioned at the exact desired coordinate, Full neutralizes that
        // offset.
        //
        // Trade-off (MS docs + 2026-05-31 repro): Full also stretches the
        // presenter to fill the carrier window. The visible menu size is
        // therefore dictated by MeasureFlyout; the 8 DIP FlyoutFrameMargin we
        // added showed up as a Mica gap at the bottom (stretched presenter not
        // consuming it as padding). See module CLAUDE.md / JOURNAL.
        _flyout.ShowAt(_frame, new FlyoutShowOptions
        {
            ShowMode = FlyoutShowMode.Transient,
            Placement = FlyoutPlacementMode.Full,
        });
        if (traceWindowing) DeckleShellTrayMenuSource.Log.FlyoutShownAt();
    }

    // ── Hide ──────────────────────────────────────────────────────────────────

    // The `reason` parameter qualifies the dismiss origin at the call site:
    // "deactivated" (activation loss), "flyout_closed" (Flyout.Closed),
    // "item_click:<label>" (item selection). Traced on the Hidden event to
    // distinguish close paths in JSONL.
    private void Hide(string reason)
    {
        if (!_isVisible) return;
        _isVisible = false;
        if (DeckleShellTrayMenuSource.IsDetailEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
        {
            DeckleShellTrayMenuSource.Log.Hidden();
            DeckleShellTrayMenuSource.Log.HiddenDetail(reason);
        }
        _flyout?.Hide();
        if (_hwnd != IntPtr.Zero)
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
    }
}
