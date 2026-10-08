// TrayQuickActionsHost — prime cycle, presenter measurement, show and hide.

using System;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Shell.TrayMenu;

public sealed partial class TrayQuickActionsHost
{
    // DesiredSize of the real FlyoutPresenter (content + its padding and
    // border), captured with the content attached to the popup tree. With
    // Placement Full the presenter stretches to the carrier, so sizing the
    // carrier to this value is what makes the visible card exactly fit: no
    // empty acrylic band, no inner scroll. Null until captured, or after the
    // content was rebuilt.
    private Windows.Foundation.Size? _presenterSize;

    // The Record button reads Record or Stop recording, two widths: a size
    // captured in one state is dropped when the panel opens in the other.
    private bool _presenterSizeIsRecording;
    private bool _syncedIsRecording;

    // ── Show ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the panel at the tray icon. A call while the panel is open closes
    /// it; a call right after a click-outside dismiss is ignored (see the
    /// class header).
    /// </summary>
    public void Show()
    {
        if (_disposed) return;

        if (_isVisible)
        {
            Hide("icon_click");
            return;
        }

        if (_lastDeactivatedHideTick != 0)
        {
            long sinceDismiss = Environment.TickCount64 - _lastDeactivatedHideTick;
            if (sinceDismiss < ReopenGuardMs)
            {
                DeckleShellTrayMenuSource.Log.PanelShowIgnored();
                DeckleShellTrayMenuSource.Log.PanelShowIgnoredDetail(sinceDismiss, ReopenGuardMs);
                return;
            }
        }

        EnsureContent();
        bool isRecording = IsRecording?.Invoke() ?? false;
        SyncState(isRecording);
        _syncedIsRecording = isRecording;
        if (_presenterSize is not null && _presenterSizeIsRecording != isRecording)
            _presenterSize = null;

        double dipW = 0, dipH = 0, usedScale = 1;
        int physW = 0, physH = 0;
        string sizeSource = "primed";

        _isVisible = true;
        _carrier.ShowAt(GetIconRect, scale =>
        {
            (dipW, dipH, sizeSource) = MeasurePanel();
            usedScale = scale;
            // Ceiling: a sub-pixel surplus is invisible, a one-pixel deficit
            // would turn on the presenter's scroll viewer.
            physW = (int)Math.Ceiling(dipW * scale);
            physH = (int)Math.Ceiling(dipH * scale);
            return (physW, physH);
        });

        // Placement Full: the panel opens exactly on the carrier, which
        // CalculatePopupWindowPosition already placed tangent to the icon.
        // ShowMode Standard (not Transient): the panel takes keyboard focus,
        // so Tab and Space work on its controls and Esc closes it.
        _flyout.ShowAt(_carrier.Frame, new FlyoutShowOptions
        {
            ShowMode = FlyoutShowMode.Standard,
            Placement = FlyoutPlacementMode.Full,
        });

        DeckleShellTrayMenuSource.Log.PanelShown();
        DeckleShellTrayMenuSource.Log.PanelShownDetail(
            _rows.Count, isRecording, dipW, dipH, physW, physH, usedScale, sizeSource);
    }

    // ── Hide ──────────────────────────────────────────────────────────────────

    /// <summary>Closes the panel, if open.</summary>
    public void Hide() => Hide("api");

    // reason: "deactivated", "flyout_closed", "icon_click", "action:<id>",
    // "api". Traced so the close paths can be told apart in the journal.
    private void Hide(string reason)
    {
        if (!_isVisible) return;
        _isVisible = false;
        DeckleShellTrayMenuSource.Log.PanelHidden();
        DeckleShellTrayMenuSource.Log.PanelHiddenDetail(reason);
        _flyout.Hide();
        _carrier.Hide();
    }

    // ── Prime cycle ───────────────────────────────────────────────────────────

    // Same reason as the context menu: the presenter's size is only right once
    // the content has been laid out inside the popup. An invisible ShowAt
    // while the carrier is still hidden, then a Hide deferred at Low priority
    // (after the layout pass), captures it once; the tree stays warm for the
    // process lifetime. Runs from the carrier's one-shot Loaded.
    private void PrimeFlyout()
    {
        // A real Show() already opened the panel: Opened captures the size.
        if (_isVisible) return;

        EnsureContent();
        bool isRecording = IsRecording?.Invoke() ?? false;
        SyncState(isRecording);
        _syncedIsRecording = isRecording;
        var sw = Stopwatch.StartNew();
        _flyout.ShowAt(_carrier.Frame, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });

        _carrier.Frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            CapturePresenterSize();
            // Do not close a panel the user opened meanwhile.
            if (!_isVisible) _flyout.Hide();

            sw.Stop();
            DeckleShellTrayMenuSource.Log.PanelPrimed();
            DeckleShellTrayMenuSource.Log.PanelPrimedDetail(
                sw.Elapsed.TotalMilliseconds,
                _presenterSize?.Width ?? 0, _presenterSize?.Height ?? 0);
        });
    }

    // A show that ran on the fallback size (prime not done yet, or content
    // rebuilt) captures the real presenter once laid out, for the next show.
    private void OnFlyoutOpened(object? sender, object e)
    {
        if (_presenterSize is not null) return;
        _carrier.Frame.DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low, CapturePresenterSize);
    }

    private void CapturePresenterSize()
    {
        if (_content is null) return;
        var presenter = FindAncestorPresenter(_content);
        if (presenter is null) return;
        presenter.UpdateLayout();
        var size = presenter.DesiredSize;
        if (size.Width > 0 && size.Height > 0)
        {
            _presenterSize = size;
            _presenterSizeIsRecording = _syncedIsRecording;
        }
    }

    // Walks up from the content root to the FlyoutPresenter hosting it. Null
    // when the content is not mounted in the popup tree.
    private static FlyoutPresenter? FindAncestorPresenter(DependencyObject start)
    {
        DependencyObject? current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is FlyoutPresenter presenter)
                return presenter;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // ── Measure ───────────────────────────────────────────────────────────────

    // Nominal path: the captured presenter size. Fallback (not captured yet):
    // the content measured on its own plus the presenter's padding and border
    // read from the FlyoutPresenter theme resources, floored by its minimum
    // size. Both in DIP.
    private (double Width, double Height, string Source) MeasurePanel()
    {
        if (_presenterSize is { } primed)
            return (primed.Width, primed.Height, "primed");

        var content = _content!;
        content.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = content.DesiredSize;

        var resources = Application.Current.Resources;
        Thickness padding = resources.TryGetValue("FlyoutContentPadding", out var p) && p is Thickness tp ? tp : default;
        Thickness border = resources.TryGetValue("FlyoutBorderThemeThickness", out var b) && b is Thickness tb ? tb : default;
        double minWidth = resources.TryGetValue("FlyoutThemeMinWidth", out var mw) && mw is double dmw ? dmw : 0;
        double minHeight = resources.TryGetValue("FlyoutThemeMinHeight", out var mh) && mh is double dmh ? dmh : 0;

        double width = desired.Width + padding.Left + padding.Right + border.Left + border.Right;
        double height = desired.Height + padding.Top + padding.Bottom + border.Top + border.Bottom;
        return (Math.Max(width, minWidth), Math.Max(height, minHeight), "fallback");
    }
}
