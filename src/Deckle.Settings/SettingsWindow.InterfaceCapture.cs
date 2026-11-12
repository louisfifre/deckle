using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Deckle.Catalog;

namespace Deckle.Settings;

// How one page of the walk went: Loaded is false when the frame did not hold
// the page, loaded, within PageLoadTimeout (the navigation threw, was refused,
// or the page never reached Loaded). LoadMs is the time that took.
public readonly record struct SettingsPageVisit(string Tag, string Label, bool Loaded, long LoadMs);

// ── Interface capture: one image per settings page ───────────────────────────
//
// The developer mode's interface capture shows each settings page in turn and
// lets the App capture the window between two pages. The walk follows the rail
// from top to bottom, footer included, through the same selection a click
// makes, so each page is built and loaded the way the person sees it. The page
// the person was on is shown again at the end, whatever happened in between.
//
// The page transition is suppressed during the walk: the entrance animation
// would otherwise still be sliding when the image is taken.
public sealed partial class SettingsWindow
{
    // How long a page gets to settle once it is loaded and has rendered:
    // values that load asynchronously, an InfoBar that opens, a list that
    // fills. Long enough for what Deckle's pages do on arrival, short enough
    // that thirty pages stay a few seconds of walk.
    private static readonly TimeSpan PageSettleDelay = TimeSpan.FromMilliseconds(300);

    // A page that is not loaded after this is captured as it is.
    private static readonly TimeSpan PageLoadTimeout = TimeSpan.FromSeconds(3);

    private bool _suppressPageTransition;

    // Calls capturePage(pageTag, railLabel) once per page, the page shown and
    // settled. Runs on the UI thread.
    public Task CaptureEachPageAsync(Func<string, string, Task> capturePage)
        => WalkPagesAsync(visit => capturePage(visit.Tag, visit.Label));

    // The walk itself: calls visit once per page with how the page loaded, the
    // page shown and settled. The self-check reads Loaded from it; the interface
    // capture ignores it and photographs whatever is there.
    //
    // starting is called with a page's tag before the window opens it: a
    // native crash while opening the page ends the process before visit could
    // say anything, so a caller that must name that page notes it here first.
    public async Task WalkPagesAsync(Func<SettingsPageVisit, Task> visit, Action<string, string>? starting = null)
    {
        object? previous = Nav.SelectedItem;
        _suppressPageTransition = true;
        try
        {
            foreach (NavigationViewItem item in PageItems())
            {
                string label = item.Content as string ?? "";
                starting?.Invoke((string)item.Tag, label);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                bool loaded = await ShowPageAsync(item);
                await visit(new SettingsPageVisit((string)item.Tag, label, loaded, clock.ElapsedMilliseconds));

                // One subpage of the page, when it names one (ISettingsSubpageSample),
                // visited the same way, then left for the rail page again.
                if (loaded && PageFrame.Content is ISettingsSubpageSample host && host.SampleSubpage() is { } sample)
                {
                    string subTag = $"{sample.Page.FullName}, {sample.Page.Assembly.GetName().Name}";
                    string subLabel = $"{label} › {sample.Label}";
                    starting?.Invoke(subTag, subLabel);
                    clock.Restart();
                    OpenSubpage(sample.Page, sample.Parameter);
                    bool subLoaded = await WaitForPageAsync(sample.Page);
                    await visit(new SettingsPageVisit(subTag, subLabel, subLoaded, clock.ElapsedMilliseconds));
                    ReturnToRailPage();
                    await WaitForPageAsync(Type.GetType((string)item.Tag));
                }
            }
        }
        finally
        {
            if (previous is NavigationViewItem back)
                await ShowPageAsync(back);
            _suppressPageTransition = false;
        }
    }

    // Every rail item that opens a page, in rail order. A node's own item has
    // no tag (its page moved down as its General child), so it is skipped.
    private IEnumerable<NavigationViewItem> PageItems()
    {
        static IEnumerable<NavigationViewItem> Walk(IEnumerable<object> items)
        {
            foreach (NavigationViewItem item in items.OfType<NavigationViewItem>())
            {
                if (item.Tag is string) yield return item;
                foreach (NavigationViewItem child in Walk(item.MenuItems))
                    yield return child;
            }
        }

        // Materialized: selecting an item can rebuild nothing here, but the
        // walk must not depend on that.
        return Walk(Nav.MenuItems).Concat(Walk(Nav.FooterMenuItems)).ToList();
    }

    // True when the page reached Loaded in the frame before the timeout.
    private async Task<bool> ShowPageAsync(NavigationViewItem item)
    {
        if (item.Tag is not string tag) return false;
        SelectPage(tag);
        return await WaitForPageAsync(Type.GetType(tag));
    }

    // True when the frame holds a loaded page of that type before the timeout;
    // the page then gets its frames and its settle delay.
    private async Task<bool> WaitForPageAsync(Type? pageType)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        bool loaded = false;
        while (waited.Elapsed < PageLoadTimeout)
        {
            if (PageFrame.Content is FrameworkElement page
                && (pageType is null || page.GetType() == pageType)
                && page.IsLoaded)
            {
                // A tag that names no type cannot be a loaded page, whatever the
                // frame holds.
                loaded = pageType is not null;
                break;
            }
            await Task.Delay(30);
        }

        await NextFrameAsync();
        await NextFrameAsync();
        await Task.Delay(PageSettleDelay);
        return loaded;
    }

    // Completes on the next composition frame, so the image is taken after
    // the layout pass has reached the screen. A window nobody can see (the
    // self-check's, kept off every monitor) may be given no frame: the wait ends
    // after a second so the walk goes on instead of hanging.
    private static async Task NextFrameAsync()
    {
        var rendered = new TaskCompletionSource();
        void OnRendering(object? sender, object e)
        {
            CompositionTarget.Rendering -= OnRendering;
            rendered.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        await Task.WhenAny(rendered.Task, Task.Delay(1000));
        CompositionTarget.Rendering -= OnRendering;
    }

    // Shows the rail page of that type, loaded and settled, for a caller that
    // captures a state of it the walk does not reach. False when it did not
    // load in time.
    public async Task<bool> ShowPageAsync(Type pageType)
    {
        _suppressPageTransition = true;
        try
        {
            SelectPage($"{pageType.FullName}, {pageType.Assembly.GetName().Name}");
            return await WaitForPageAsync(pageType);
        }
        finally
        {
            _suppressPageTransition = false;
        }
    }

    // The page the frame holds now, for a caller that renders it itself.
    public FrameworkElement? CurrentPage => PageFrame.Content as FrameworkElement;
}
