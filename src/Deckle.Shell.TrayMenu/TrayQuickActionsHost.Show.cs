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
    // carrier to this value makes the visible card fit exactly: no empty
    // acrylic band, no inner scroll. Null until captured, or after the
    // content was rebuilt.
    //
    // The Record button reads Record or Stop recording, two widths, so each
    // recording state keeps its own captured size. Stored only when the
    // presenter holds the content (TrayMenuSizing.HoldsItems): a degenerate
    // measure reports the presenter's minimum size.
    private Windows.Foundation.Size? _presenterSizeIdle;
    private Windows.Foundation.Size? _presenterSizeRecording;
    private bool _syncedIsRecording;

    private ref Windows.Foundation.Size? PresenterSizeFor(bool isRecording)
    {
        if (isRecording) return ref _presenterSizeRecording;
        return ref _presenterSizeIdle;
    }

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

        if (_lastDismissTick != 0)
        {
            long sinceDismiss = Environment.TickCount64 - _lastDismissTick;
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

        // Hidden before the open completed: the carrier lost activation or
        // the flyout closed by itself during ShowAt. The user saw nothing;
        // the warning says so whatever the Windowing verbosity.
        if (!_isVisible)
        {
            DeckleShellTrayMenuSource.Log.PanelDismissedWhileOpening();
            DeckleShellTrayMenuSource.Log.PanelDismissedWhileOpeningDetail(_lastHideReason);
            return;
        }

        DeckleShellTrayMenuSource.Log.PanelShown();
        DeckleShellTrayMenuSource.Log.PanelShownDetail(
            _rows.Count, isRecording, dipW, dipH, physW, physH, usedScale, sizeSource);
    }

    // ── Hide ──────────────────────────────────────────────────────────────────

    public void Hide() => Hide("api");

    private void Hide(string reason)
    {
        if (!_isVisible) return;
        _isVisible = false;
        _lastHideReason = reason;
        DeckleShellTrayMenuSource.Log.PanelHidden();
        DeckleShellTrayMenuSource.Log.PanelHiddenDetail(reason);
        _flyout.Hide();
        _carrier.Hide();
    }

    // ── Prime cycle ───────────────────────────────────────────────────────────

    // The presenter's size is only right once the content has been laid out
    // inside the popup. An invisible ShowAt while the carrier is still hidden,
    // then a Hide deferred at Low priority (after the layout pass), captures
    // it; the tree stays warm for the process lifetime.
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
            CapturePresenterSize("prime");
            // Do not close a panel the user opened meanwhile.
            if (!_isVisible) _flyout.Hide();

            sw.Stop();
            DeckleShellTrayMenuSource.Log.PanelPrimed();
            DeckleShellTrayMenuSource.Log.PanelPrimedDetail(
                sw.Elapsed.TotalMilliseconds,
                PresenterSizeFor(_syncedIsRecording)?.Width ?? 0,
                PresenterSizeFor(_syncedIsRecording)?.Height ?? 0);
        });
    }

    // Every real open re-captures the presenter once laid out, as the context
    // menu does, so a bad value from one capture heals on the next open
    // instead of sticking for the process lifetime.
    private void OnFlyoutOpened(object? sender, object e)
    {
        _carrier.Frame.DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low, () => CapturePresenterSize("opened"));
    }

    // Keeps the presenter size only when it holds the content measured in the
    // same layout: a presenter measured clipped, or before its content was
    // laid out, reports its minimum size, which is positive and would size
    // the carrier to a card too small for the rows. phase: "prime", "opened".
    private void CapturePresenterSize(string phase)
    {
        if (_content is null) return;
        var presenter = FindAncestorPresenter(_content);
        if (presenter is null) return;
        presenter.UpdateLayout();

        var content = (_content.DesiredSize.Width, _content.DesiredSize.Height);
        // Content not laid out yet: nothing to check against, keep what was there.
        if (content.Height <= 0) return;

        var captured = (presenter.DesiredSize.Width, presenter.DesiredSize.Height);
        if (TrayMenuSizing.HoldsItems(captured, [content]))
        {
            PresenterSizeFor(_syncedIsRecording) = presenter.DesiredSize;
            return;
        }

        DeckleShellTrayMenuSource.Log.PanelSizeRejected();
        DeckleShellTrayMenuSource.Log.PanelSizeRejectedDetail(
            phase, captured.Width, captured.Height, content.Width, content.Height);
    }

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
        if (PresenterSizeFor(_syncedIsRecording) is { } primed)
            return (primed.Width, primed.Height, "primed");

        var content = _content!;
        content.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = content.DesiredSize;

        var resources = Application.Current.Resources;
        Thickness padding = resources.TryGetValue("FlyoutContentThemePadding", out var p) && p is Thickness tp ? tp : default;
        Thickness border = resources.TryGetValue("FlyoutBorderThemeThickness", out var b) && b is Thickness tb ? tb : default;
        double minWidth = resources.TryGetValue("FlyoutThemeMinWidth", out var mw) && mw is double dmw ? dmw : 0;
        double minHeight = resources.TryGetValue("FlyoutThemeMinHeight", out var mh) && mh is double dmh ? dmh : 0;

        double width = desired.Width + padding.Left + padding.Right + border.Left + border.Right;
        double height = desired.Height + padding.Top + padding.Bottom + border.Top + border.Bottom;
        return (Math.Max(width, minWidth), Math.Max(height, minHeight), "fallback");
    }
}
