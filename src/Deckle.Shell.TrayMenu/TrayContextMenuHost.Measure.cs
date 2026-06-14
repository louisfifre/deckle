// TrayContextMenuHost — prime cycle, flyout measurement, presenter capture.

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
        var sw = Stopwatch.StartNew();
        _flyout!.ShowAt(_frame, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });

        _frame!.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            // Capture DesiredSize for items attached to the popup visual tree,
            // after forcing NarrowPadding state on each item. The framework
            // switches to NarrowPadding anyway as soon as a mouse/pen/keyboard
            // pointer interacts with the menu (see PaddingSizeStates
            // VisualState in DefaultMenuFlyoutItemStyle, WindowsAppSDK
            // generic.xaml line 24058); we accelerate that switch during the
            // prime cycle so the cache reflects the final size (≈ 32 DIP/item)
            // rather than the initial DefaultPadding size (≈ 40). Without this
            // force, the carrier window was sized to the initial size while the
            // internal popup (following NarrowPadding state) rendered more
            // compact, creating a visible Mica gap at the bottom.
            if (_flyout is not null)
            {
                foreach (var item in _flyout.Items)
                {
                    if (item is MenuFlyoutItem mfi)
                        VisualStateManager.GoToState(mfi, "NarrowPadding", useTransitions: false);
                }
                // Force a layout pass so the new Padding values applied by the
                // VisualState Storyboard are effective in the DesiredSize we
                // are about to capture.
                _frame!.UpdateLayout();

                _primedSizes.Clear();
                foreach (var item in _flyout.Items)
                    _primedSizes[item] = item.DesiredSize;

                // Capture the real presenter size (walking up from the first
                // item, attached at this point). Its DesiredSize includes its
                // padding + border, so it exactly reflects the visible card; in
                // contrast, the item sum ignores those and we compensated with
                // an imprecise flat margin.
                _primedPresenterSize = null;
                if (_flyout.Items.Count > 0)
                {
                    var presenter = FindAncestorPresenter(_flyout.Items[0]);
                    if (presenter is not null)
                    {
                        presenter.UpdateLayout();
                        _primedPresenterSize = presenter.DesiredSize;
                    }
                }
            }

            _flyout?.Hide();
            sw.Stop();
            DeckleShellTrayMenuSource.Log.PrimeCycleCompleted();
            DeckleShellTrayMenuSource.Log.PrimeCycleCompletedDetail(sw.Elapsed.TotalMilliseconds);
        });
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

    // ── Measure ───────────────────────────────────────────────────────────────
    //
    // Sizes the carrier window to the real MenuFlyoutPresenter DesiredSize,
    // captured during the prime cycle (_primedPresenterSize). This size
    // includes the presenter's own padding and border, so it exactly matches
    // the card painted by the popup. Because Full stretches the presenter to
    // the carrier window, sizing the window to this value makes stretching
    // neutral: no Mica gap (oversize), no scroll (undersize).
    //
    // The loop below remains for per-item diagnostics (ItemAttachmentChecked /
    // ItemMeasured events in JSONL) and to feed the fallback. DesiredSize
    // values are read from the _primedSizes cache (items attached during the
    // prime cycle) rather than through detached item.Measure(), which returns
    // unstable values (see module JOURNAL.md).
    //
    // Fallback (presenter not captured during prime, or prime not yet run):
    // sum of item heights + FlyoutFrameMargin × 2. Historical, imprecise path
    // (the 8 DIP flat margin overestimated the presenter's real chrome
    // ≈ 4-6 DIP, causing the gap); kept as a guard against zero-size popup.

    private (int width, int height) MeasureFlyout(double scale)
    {
        if (_flyout is null) return (0, 0);

        double width = 0;
        double height = 0;
        int idx = 0;
        foreach (var item in _flyout.Items)
        {
            string itemText = item switch
            {
                MenuFlyoutItem mi => mi.Text,
                MenuFlyoutSeparator => "<separator>",
                _ => "<unknown>",
            };

            Windows.Foundation.Size desired;
            if (_primedSizes.TryGetValue(item, out var cached))
            {
                desired = cached;
            }
            else
            {
                // Safety fallback: the prime cycle has not populated the cache
                // yet. Detached measurement is accepted for lack of a better
                // option; at worst the popup displays the native compressed
                // height.
                item.Measure(new Windows.Foundation.Size(10_000, 10_000));
                desired = item.DesiredSize;
            }
            width = Math.Max(width, desired.Width);
            height += desired.Height;

            DeckleShellTrayMenuSource.Log.ItemMeasured(
                idx, itemText, item.GetType().Name,
                desired.Width, desired.Height);
            idx++;
        }

        double dipW;
        double dipH;
        if (_primedPresenterSize is { } presenterSize
            && presenterSize.Width > 0 && presenterSize.Height > 0)
        {
            // Exact size of the real presenter; Full has nothing left to stretch.
            dipW = presenterSize.Width;
            dipH = presenterSize.Height;
        }
        else
        {
            // Imprecise fallback: item sum + flat margin.
            dipW = width + FlyoutFrameMargin * 2;
            dipH = height + FlyoutFrameMargin * 2;
        }

        // Ceiling rather than truncation: prefer a possible sub-pixel gap
        // (invisible) over a one-pixel undersize that would reactivate
        // presenter scrolling.
        int physW = (int)Math.Ceiling(dipW * scale);
        int physH = (int)Math.Ceiling(dipH * scale);

        DeckleShellTrayMenuSource.Log.FlyoutMeasured(dipW, dipH, physW, physH, scale);

        return (physW, physH);
    }
}
