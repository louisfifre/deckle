using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Settings;

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
    public async Task CaptureEachPageAsync(Func<string, string, Task> capturePage)
    {
        object? previous = Nav.SelectedItem;
        _suppressPageTransition = true;
        try
        {
            foreach (NavigationViewItem item in PageItems())
            {
                await ShowPageAsync(item);
                await capturePage((string)item.Tag, item.Content as string ?? "");
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

    private async Task ShowPageAsync(NavigationViewItem item)
    {
        if (item.Tag is not string tag) return;
        SelectPage(tag);

        Type? pageType = Type.GetType(tag);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (waited.Elapsed < PageLoadTimeout)
        {
            if (PageFrame.Content is FrameworkElement page
                && (pageType is null || page.GetType() == pageType)
                && page.IsLoaded)
                break;
            await Task.Delay(30);
        }

        await NextFrameAsync();
        await NextFrameAsync();
        await Task.Delay(PageSettleDelay);
    }

    // Completes on the next composition frame, so the image is taken after
    // the layout pass has reached the screen.
    private static Task NextFrameAsync()
    {
        var rendered = new TaskCompletionSource();
        void OnRendering(object? sender, object e)
        {
            CompositionTarget.Rendering -= OnRendering;
            rendered.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        return rendered.Task;
    }
}
