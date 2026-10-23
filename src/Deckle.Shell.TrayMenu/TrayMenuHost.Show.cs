// TrayMenuHost — anchor on the icon, side by taskbar edge, show and hide.

using System;
using Deckle.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;

namespace Deckle.Shell.TrayMenu;

public sealed partial class TrayMenuHost
{
    // Half extent of the anchor square around the cursor when the shell does
    // not know the icon's rect, in physical pixels. 36 px is a tray icon slot
    // at 100 %: the menu then opens beside the cursor, not under it.
    private const int CursorAnchorHalfExtent = 18;

    // ── Show ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the menu beside the tray icon. A call while the menu is open
    /// closes it; a call right after a dismiss is ignored (class header).
    /// </summary>
    public void Show()
    {
        if (_disposed) return;

        if (_isVisible)
        {
            Hide("icon_click");
            return;
        }

        if (_lastDismissTick != 0)
        {
            long sinceDismiss = Environment.TickCount64 - _lastDismissTick;
            if (sinceDismiss < ReopenGuardMs)
            {
                DeckleShellTrayMenuSource.Log.ShowIgnoredAfterDismiss();
                DeckleShellTrayMenuSource.Log.ShowIgnoredAfterDismissDetail(sinceDismiss, ReopenGuardMs);
                return;
            }
        }

        DeckleShellTrayMenuSource.Log.ShowRequested();
        SyncItems();

        NativeMethods.RECT anchor = ResolveAnchor();
        TrayMenuSide side = ChooseSide(anchor);

        // The anchor window sits exactly on the icon; the menu opens on the
        // side of it that faces the work area, at its natural size, in its own
        // popup window, its near corner on the icon (see ToPlacement). WinUI
        // keeps it inside the work area when the icon is near a corner.
        _isVisible = true;
        _carrier.ShowAt(anchor);
        _flyout.ShowAt(_carrier.Frame, new FlyoutShowOptions
        {
            ShowMode = FlyoutShowMode.Transient,
            Placement = ToPlacement(side),
        });

        DeckleShellTrayMenuSource.Log.FlyoutShownAt();
        DeckleShellTrayMenuSource.Log.FlyoutShownAtDetail(
            _windowName, side.ToString(), anchor.left, anchor.top, anchor.right, anchor.bottom);
    }

    // The real tray icon rect (Shell_NotifyIconGetRect) when the shell knows
    // it; otherwise a square around the cursor (icon not registered yet, hidden
    // in the overflow, explorer.exe restarting).
    private NativeMethods.RECT ResolveAnchor()
    {
        if (GetIconRect?.Invoke() is { } icon) return icon;

        NativeMethods.GetCursorPos(out POINT cursor);
        return new NativeMethods.RECT
        {
            left   = cursor.X - CursorAnchorHalfExtent,
            top    = cursor.Y - CursorAnchorHalfExtent,
            right  = cursor.X + CursorAnchorHalfExtent,
            bottom = cursor.Y + CursorAnchorHalfExtent,
        };
    }

    // The side of the anchor that faces the work area of the monitor under it:
    // the taskbar is the band outside that work area, and the icon sits in
    // the band. DisplayArea gives the work area in physical pixels, the same
    // space as the icon rect.
    private static TrayMenuSide ChooseSide(NativeMethods.RECT anchor)
    {
        var center = new PointInt32((anchor.left + anchor.right) / 2, (anchor.top + anchor.bottom) / 2);
        RectInt32 work = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest).WorkArea;
        return TrayMenuPlacement.Choose(
            (anchor.left, anchor.top, anchor.right, anchor.bottom),
            (work.X, work.Y, work.X + work.Width, work.Y + work.Height));
    }

    // Edge-aligned, not centered: a native tray menu (TrackPopupMenu with
    // TPM_LEFTALIGN | TPM_BOTTOMALIGN, what the carrier used before the
    // 2026-10 rewrite) grows from the icon's corner, its bottom-left corner on
    // the icon for a taskbar at the bottom. The centered modes (Top, Left...)
    // put the menu's middle on the icon, which reads as "opened at the
    // click" rather than "opened from the icon" (Louis, 2026-10-14). Above a
    // bottom bar and below a top bar the menu keeps the icon's left edge; beside
    // a side bar it keeps the icon's bottom edge.
    private static FlyoutPlacementMode ToPlacement(TrayMenuSide side) => side switch
    {
        TrayMenuSide.Below   => FlyoutPlacementMode.BottomEdgeAlignedLeft,
        TrayMenuSide.LeftOf  => FlyoutPlacementMode.LeftEdgeAlignedBottom,
        TrayMenuSide.RightOf => FlyoutPlacementMode.RightEdgeAlignedBottom,
        _                    => FlyoutPlacementMode.TopEdgeAlignedLeft,
    };

    // ── Hide ──────────────────────────────────────────────────────────────────

    /// <summary>Closes the menu, if open.</summary>
    public void Hide() => Hide("api");

    // reason: "deactivated", "flyout_closed", "icon_click", "item_click:<id>",
    // "api". Traced so the close paths can be told apart in the log.
    private void Hide(string reason)
    {
        if (!_isVisible) return;
        _isVisible = false;
        DeckleShellTrayMenuSource.Log.Hidden();
        DeckleShellTrayMenuSource.Log.HiddenDetail(reason);
        _flyout.Hide();
        _carrier.Hide();
    }
}
