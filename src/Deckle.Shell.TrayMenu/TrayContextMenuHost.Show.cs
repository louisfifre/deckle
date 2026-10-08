// TrayContextMenuHost — show on the carrier, and hide.

using System;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Shell.TrayMenu;

public sealed partial class TrayContextMenuHost
{
    // ── Show ──────────────────────────────────────────────────────────────────

    // Menu items are static (no module state to reflect), so a show is only
    // anchor + measure + open.

    public void Show()
    {
        if (_disposed || _flyout is null)
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

        // The carrier anchors on the icon, resolves the monitor DPI and asks
        // for the menu's physical size at that scale.
        _isVisible = true;
        _carrier.ShowAt(GetIconRect, MeasureFlyout);

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
        _flyout.ShowAt(_carrier.Frame, new FlyoutShowOptions
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
        _carrier.Hide();
    }
}
