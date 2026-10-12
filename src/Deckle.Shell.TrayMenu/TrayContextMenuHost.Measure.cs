// TrayContextMenuHost — prime cycle, size capture, flyout measurement.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using Deckle.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Shell.TrayMenu;

public sealed partial class TrayContextMenuHost
{
    // ── Prime cycle ────────────────────────────────────────────────────────────

    // Prime measure: prime the visual tree so native MenuFlyoutItems have
    // their ControlTemplate applied and DesiredSize measurable on the first
    // real Show(). A synchronous ShowAt + Hide cycle is insufficient:
    // 2026-05-25 app.jsonl observation: show_count=1 measured desired_w/h=0
    // for all native items. Cause: immediate synchronous Hide cuts the
    // prime before WinUI's layout pass has run on MenuFlyoutPresenter
    // items. Fix: defer Hide through DispatcherQueue.TryEnqueue(Low); Low
    // priority inserts the callback after the layout pass and initial
    // popup render frame have occurred. At that point each item has its
    // correct DesiredSize, and the visual tree remains "warmed" for the
    // process lifetime.
    private void PrimeFlyout()
    {
        // A real Show() already opened the menu: Opened captures the sizes.
        if (_isVisible) return;

        bool traceWindowing = DeckleShellTrayMenuSource.IsDetailEnabled(
            EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle);
        var sw = traceWindowing ? Stopwatch.StartNew() : null;
        _flyout!.ShowAt(_carrier.Frame, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });

        _carrier.Frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            CaptureSizes("prime");

            // Do not close a menu the user opened meanwhile.
            if (!_isVisible) _flyout?.Hide();

            if (sw is not null)
            {
                sw.Stop();
                DeckleShellTrayMenuSource.Log.PrimeCycleCompleted();
                DeckleShellTrayMenuSource.Log.PrimeCycleCompletedDetail(sw.Elapsed.TotalMilliseconds);
            }
        });
    }

    // ── Size capture ──────────────────────────────────────────────────────────

    // Captures the item sizes and the real presenter size with the items
    // attached to the popup tree. Runs after the prime cycle's layout pass,
    // and again after every real open (OnFlyoutOpened), so a bad value from
    // one capture heals on the next open instead of sticking for the process
    // lifetime.
    //
    // NarrowPadding is forced first. The framework switches to it anyway as
    // soon as a mouse/pen/keyboard pointer interacts with the menu
    // (PaddingSizeStates in DefaultMenuFlyoutItemStyle, WindowsAppSDK
    // generic.xaml); forcing it here makes the capture reflect the final size
    // (≈ 32 DIP/item) rather than the initial DefaultPadding size (≈ 40).
    //
    // phase: "prime" or "opened", traced when a capture is rejected.
    private void CaptureSizes(string phase)
    {
        if (_flyout is null) return;

        foreach (var item in _flyout.Items)
        {
            if (item is MenuFlyoutItem mfi)
                VisualStateManager.GoToState(mfi, "NarrowPadding", useTransitions: false);
        }
        // Force a layout pass so the padding set by the VisualState is in the
        // DesiredSize values read below.
        _carrier.Frame.UpdateLayout();

        // Item sizes: kept only when every visible item measured a height.
        // A capture taken before the items were laid out reads zeros (the
        // 2026-05-25 observation); it must not replace an earlier good one.
        // Without them the presenter size cannot be checked either.
        bool itemsLaidOut = _flyout.Items
            .Where(i => i.Visibility == Visibility.Visible)
            .All(i => i.DesiredSize.Height > 0);
        if (!itemsLaidOut) return;

        _primedSizes.Clear();
        foreach (var item in _flyout.Items)
            _primedSizes[item] = item.DesiredSize;

        // Presenter size, walking up from the first VISIBLE item (a collapsed
        // item may never mount into the popup tree and would dead-end the
        // walk). Its DesiredSize includes the presenter's padding and border,
        // so it matches the visible card; the item sum ignores them.
        var firstVisible = _flyout.Items
            .FirstOrDefault(i => i.Visibility == Visibility.Visible);
        if (firstVisible is null) return;
        var presenter = FindAncestorPresenter(firstVisible);
        if (presenter is null) return;
        presenter.UpdateLayout();
        var captured = (presenter.DesiredSize.Width, presenter.DesiredSize.Height);

        var items = CurrentItemSizes(traceWindowing: false);
        if (TrayMenuSizing.HoldsItems(captured, items))
        {
            _primedPresenterSize = captured;
            return;
        }

        // A presenter smaller than its items was measured clipped or before
        // its items were laid out (it then reports its MinHeight, one item
        // tall). Keep whatever was there; MeasureFlyout re-checks it anyway.
        var (itemsW, itemsH) = TrayMenuSizing.ItemExtent(items);
        DeckleShellTrayMenuSource.Log.PresenterSizeRejected();
        DeckleShellTrayMenuSource.Log.PresenterSizeRejectedDetail(
            phase, captured.Width, captured.Height, itemsW, itemsH);
    }

    // Walks up the visual tree from a descendant to the MenuFlyoutPresenter
    // that hosts popup items. Returns null if the tree is not mounted yet
    // (presenter absent from the tree at call time).
    private static MenuFlyoutPresenter? FindAncestorPresenter(DependencyObject start)
    {
        DependencyObject? current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is MenuFlyoutPresenter presenter)
                return presenter;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // The size of every item, from the attached capture when there is one.
    // Without a capture (the prime cycle has not run yet), the item is
    // measured detached, accepted for lack of a better option: detached
    // measures are unstable (see module JOURNAL.md).
    private List<(double Width, double Height)> CurrentItemSizes(bool traceWindowing)
    {
        var sizes = new List<(double Width, double Height)>();
        if (_flyout is null) return sizes;

        int idx = 0;
        foreach (var item in _flyout.Items)
        {
            Windows.Foundation.Size desired;
            if (_primedSizes.TryGetValue(item, out var cached))
            {
                desired = cached;
            }
            else
            {
                item.Measure(new Windows.Foundation.Size(10_000, 10_000));
                desired = item.DesiredSize;
            }
            sizes.Add((desired.Width, desired.Height));

            if (traceWindowing)
            {
                string itemText = item switch
                {
                    MenuFlyoutItem mi => mi.Text,
                    MenuFlyoutSeparator => "<separator>",
                    _ => "<unknown>",
                };
                DeckleShellTrayMenuSource.Log.ItemMeasured(
                    idx, itemText, item.GetType().Name,
                    desired.Width, desired.Height);
            }
            idx++;
        }
        return sizes;
    }

    // ── Measure ───────────────────────────────────────────────────────────────
    //
    // Placement Full stretches the presenter to the carrier window, so the
    // carrier size IS the visible card. TrayMenuSizing.Decide picks it: the
    // captured presenter size when it holds every item, else the item extent
    // plus a frame margin. Returns physical pixels at the anchor monitor's
    // scale.

    private (int width, int height) MeasureFlyout(double scale)
    {
        if (_flyout is null) return (0, 0);

        bool traceWindowing = DeckleShellTrayMenuSource.IsDetailEnabled(
            EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle);

        var items = CurrentItemSizes(traceWindowing);
        var size = TrayMenuSizing.Decide(_primedPresenterSize, items);

        // Ceiling rather than truncation: prefer a possible sub-pixel gap
        // (invisible) over a one-pixel undersize that would reactivate
        // presenter scrolling.
        int physW = (int)Math.Ceiling(size.Width * scale);
        int physH = (int)Math.Ceiling(size.Height * scale);

        if (traceWindowing)
        {
            DeckleShellTrayMenuSource.Log.FlyoutMeasured(
                size.Width, size.Height, physW, physH, scale,
                size.Source == TrayMenuSizing.SizeSource.Presenter ? "presenter" : "items");
        }

        return (physW, physH);
    }
}
