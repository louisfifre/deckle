// TrayContextMenuHost — show on the carrier, and hide.

using System;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Shell.TrayMenu;

public sealed partial class TrayContextMenuHost
{
    // ── Show ──────────────────────────────────────────────────────────────────

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

        _isVisible = true;
<<<<<<< HEAD
        _carrier.ShowAt(GetIconRect, MeasureFlyout);

        // FlyoutPlacementMode.Full: the default Top placement would add its
        // own offset above the frame on top of CalculatePopupWindowPosition's,
        // and the menu would jump up by about one menu height. Full also
        // stretches the presenter to fill the carrier (MS docs), so
        // MeasureFlyout dictates the visible size: too large shows an empty
        // Mica band, too small scrolls.
        _flyout.ShowAt(_carrier.Frame, new FlyoutShowOptions
=======
        _carrier.ShowAt(GetIconRect, MeasureFlyout, () =>
>>>>>>> main
        {
            // The carrier runs this once its XAML island reports the size it
            // was just given, or after its grace period: Full reads that size
            // at ShowAt (see TrayCarrierWindow). A Hide meanwhile cancels it.
            if (_disposed || !_isVisible || _flyout is null) return;

            // FlyoutPlacementMode.Full: opens the menu at the exact target
            // location (our frame). Without Full, default Top placement places
            // the popup above the frame, adding a vertical offset on top of the
            // one already calculated by CalculatePopupWindowPosition; the menu
            // jumps up by roughly one menu height. Since the carrier window is
            // already positioned at the exact desired coordinate, Full
            // neutralizes that offset.
            //
            // Trade-off (MS docs): Full also stretches the presenter to fill
            // the carrier window. The visible menu size is therefore dictated
            // by MeasureFlyout: too large shows an empty Mica band at the
            // bottom, too small scrolls. See TrayMenuSizing.
            _flyout.ShowAt(_carrier.Frame, new FlyoutShowOptions
            {
                ShowMode = FlyoutShowMode.Transient,
                Placement = FlyoutPlacementMode.Full,
            });
            if (traceWindowing) DeckleShellTrayMenuSource.Log.FlyoutShownAt();
        });
    }

    // ── Hide ──────────────────────────────────────────────────────────────────

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
