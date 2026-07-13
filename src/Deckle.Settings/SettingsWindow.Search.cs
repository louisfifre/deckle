using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VirtualKey = Windows.System.VirtualKey;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Settings;

// ─── Settings cross-page search ───────────────────────────────────────────────
//
// The TitleBar's AutoSuggestBox reaches any setting on any page. It queries the
// shell index (SettingsSearchIndex, populated at boot from every page's manifest),
// renders the top hits with the matched terms in bold, and on selection navigates
// to the hit's page and scrolls its card into view. The box never opens a standalone
// results page: Enter without a chosen hit is a no-op by spec, and an overflow beyond
// the cap surfaces as an inert "+N more — refine" hint, not a "see all" link.
//
// Presentation is measure-driven, not window-width-driven: the TitleBar's central
// zone is its one star column, so the zone's own width IS the space the bar can
// afford. Wide, the box stretches with the zone up to its cap; when the zone drops
// below the box's useful floor it swaps for the search icon; clicking the icon
// floats the SAME box in the SearchOverlay below the bar (reparented in, and back
// out on dismissal — one control, one state). At extreme widths the TitleBar's own
// Compact visual state reclaims the title area natively; no title juggling here.

public sealed partial class SettingsWindow
{
    // The box's useful floor and the restore width above it. Zone widths in DIPs;
    // the 24 gap is hysteresis so the swap cannot oscillate on a boundary pixel.
    // Element-measured, so title length, DPI and the Logs command all price
    // themselves in without a window-width constant.
    private const double SearchIconZoneWidth = 180.0;
    private const double SearchInlineZoneWidth = 204.0;

    private enum SearchPresentation { Inline, Icon, Overlay }
    private SearchPresentation _searchPresentation = SearchPresentation.Inline;

    // Rebuilt (created lazily) on first keystroke; a single dedicated debounce timer.
    private DispatcherQueueTimer? _searchDebounce;

    // Debounce window before a query runs: long enough to skip mid-word passes,
    // short enough to feel immediate once the user pauses.
    private const int SearchDebounceMs = 300;

    // Rendered-hit cap. A total beyond this appends a "+N more — refine" notice
    // rather than growing the list — the search stays a shortcut, not a browser.
    private const int MaxSuggestions = 7;

    private void InitializeSearch()
    {
        // Click-away: page background and nav chrome are not focusable, so a press
        // there never pulls focus off the box on its own. Observe every press
        // (handledEventsToo) and release the search focus when one lands outside
        // the search surfaces.
        RootGrid.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(OnRootPointerPressed), handledEventsToo: true);

        // The TitleBar sits first in tab order, so initial focus would land in the
        // box and swallow the first keystrokes. Hand it to the content instead.
        RootGrid.Loaded += (_, _) => PageFrame.Focus(FocusState.Programmatic);

        InitializeTitleBarProbe();
    }

    // ── Presentation: inline box ↔ icon ↔ floating overlay ───────────────────

    private void OnSearchZoneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double zone = e.NewSize.Width;
        switch (_searchPresentation)
        {
            case SearchPresentation.Inline when zone < SearchIconZoneWidth:
                SetSearchPresentation(SearchPresentation.Icon, zone);
                break;
            // The overlay only exists because the zone could not host the box; once
            // the zone can again, fold it straight back into the bar.
            case SearchPresentation.Icon or SearchPresentation.Overlay
                when zone >= SearchInlineZoneWidth:
                SetSearchPresentation(SearchPresentation.Inline, zone);
                break;
        }
        ScheduleTitleBarProbe();
    }

    private void OnSearchIconClick(object sender, RoutedEventArgs e)
    {
        SetSearchPresentation(SearchPresentation.Overlay, SearchZone.ActualWidth);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void SetSearchPresentation(SearchPresentation target, double zoneWidth)
    {
        if (target == _searchPresentation) return;
        DeckleSettingsSource.Log.SearchPresentationChanged(
            _searchPresentation.ToString(), target.ToString(), (int)zoneWidth);
        _searchPresentation = target;

        // The box lives in exactly one of two hosts: the TitleBar zone or the
        // overlay shell. Reparent first, then set the visibilities of the state.
        if (target == SearchPresentation.Overlay)
        {
            SearchZone.Children.Remove(SearchBox);
            SearchOverlay.Child = SearchBox;
            SearchBox.Visibility = Visibility.Visible;
            SearchOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            if (SearchOverlay.Child == SearchBox)
            {
                SearchOverlay.Child = null;
                SearchZone.Children.Insert(0, SearchBox);
            }
            SearchOverlay.Visibility = Visibility.Collapsed;
            SearchBox.Visibility = target == SearchPresentation.Inline
                ? Visibility.Visible : Visibility.Collapsed;
        }
        SearchIconButton.Visibility = target == SearchPresentation.Inline
            ? Visibility.Collapsed : Visibility.Visible;

        ScheduleTitleBarProbe();
    }

    // ── Focus release ────────────────────────────────────────────────────────
    //
    // The box holds focus until something focusable takes it, which a Mica
    // backdrop or a page background never does — so give focus explicit exits:
    // Escape, and any press outside the search surfaces.

    private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // First Escape closes the suggestion list (handled by the box itself);
        // the next one releases focus and retracts the overlay.
        if (e.Key != VirtualKey.Escape || SearchBox.IsSuggestionListOpen) return;
        ReleaseSearchFocus("escape");
        e.Handled = true;
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsFocusInSearch()) return;
        if (e.OriginalSource is DependencyObject src &&
            (IsWithin(src, SearchZone) || IsWithin(src, SearchOverlay))) return;
        ReleaseSearchFocus("pointer");
    }

    private void OnSearchBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_searchPresentation != SearchPresentation.Overlay) return;
        // Focus transits through the suggestion flyout mid-selection; decide on the
        // settled state, one dispatcher tick later, not on the transient.
        DispatcherQueue.TryEnqueueObserved(
            operation: "ui-update", caller: "settings-search-dismiss",
            callback: () =>
            {
                if (_searchPresentation != SearchPresentation.Overlay) return;
                if (IsFocusInSearch() || SearchBox.IsSuggestionListOpen) return;
                SetSearchPresentation(SearchPresentation.Icon, SearchZone.ActualWidth);
            },
            rejectSource: "SETTINGS", rejectWhat: "search overlay dismissal");
    }

    private void ReleaseSearchFocus(string via)
    {
        if (_searchPresentation == SearchPresentation.Overlay)
            SetSearchPresentation(SearchPresentation.Icon, SearchZone.ActualWidth);
        // The Frame takes programmatic focus; if a page ever refuses it, fall back
        // to wherever tab order goes next — anywhere out of the box will do.
        if (!PageFrame.Focus(FocusState.Programmatic))
            _ = FocusManager.TryMoveFocusAsync(FocusNavigationDirection.Next);
        DeckleSettingsSource.Log.SearchFocusReleased(via);
    }

    private bool IsFocusInSearch()
        => FocusManager.GetFocusedElement(Content.XamlRoot) is DependencyObject focused &&
           (IsWithin(focused, SearchZone) || IsWithin(focused, SearchOverlay));

    private static bool IsWithin(DependencyObject node, DependencyObject root)
    {
        for (DependencyObject? d = node; d is not null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, root)) return true;
        return false;
    }

    // ── Query → suggestions ──────────────────────────────────────────────────

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Only react to typing. A programmatic set (SuggestionChosen writing the label
        // back, or the post-navigation clear) must not re-run the search.
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        if (_searchDebounce is null)
        {
            _searchDebounce = DispatcherQueue.CreateTimer();
            _searchDebounce.Interval = TimeSpan.FromMilliseconds(SearchDebounceMs);
            _searchDebounce.IsRepeating = false;
            _searchDebounce.Tick += (_, _) => RunSearch();
        }
        // Restart the window on every keystroke; IsRepeating=false self-stops on Tick.
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void RunSearch()
    {
        string query = SearchBox.Text ?? string.Empty;
        string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            // Cleared to whitespace: drop the dropdown rather than show every card.
            SearchBox.ItemsSource = null;
            return;
        }

        (IReadOnlyList<SettingSearchHit> hits, int total) =
            SettingsSearchIndex.Search(query, MaxSuggestions);
        DeckleSettingsSource.Log.SearchExecuted(query.Length, total);

        var rows = new List<SettingSuggestion>(hits.Count + 1);
        foreach (SettingSearchHit hit in hits)
            rows.Add(ToSuggestion(hit, tokens));

        // A terminal, non-navigating row closes the list: nothing found, or a count
        // beyond what is shown. N is total minus what fits, so it stays exact at the cap.
        if (total == 0)
            rows.Add(new SettingSuggestionNotice { Text = Loc.Get("Settings_Search_NoResults") });
        else if (total > hits.Count)
            rows.Add(new SettingSuggestionNotice
            {
                Text = Loc.Format("Settings_Search_MoreResults_Format", total - hits.Count),
            });

        SearchBox.ItemsSource = rows;
    }

    private static SettingSuggestionHit ToSuggestion(SettingSearchHit hit, string[] tokens)
        => new()
        {
            PageTag = hit.PageTag,
            CardTag = hit.CardTag,
            Glyph = hit.PageGlyph,
            Label = hit.Label,
            LabelSegments = SegmentLabel(hit.Label, tokens),
            Secondary = hit.Description is null
                ? hit.PageLabel
                : $"{hit.PageLabel} · {hit.Description}",
        };

    // Splits the label into consecutive matched / unmatched runs. Every case-insensitive
    // occurrence of every query token is marked, then adjacent chars of equal state are
    // coalesced — so "the" in "Theme" bolds the prefix, and repeated tokens all bold.
    // Only the label is emboldened; a hit that matched solely on a keyword or description
    // shows no bold, which honestly signals the match came from elsewhere.
    private static IReadOnlyList<SuggestionTextSegment> SegmentLabel(string label, string[] tokens)
    {
        var matched = new bool[label.Length];
        foreach (string token in tokens)
        {
            int from = 0;
            while (from <= label.Length - token.Length)
            {
                int idx = label.IndexOf(token, from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                for (int i = idx; i < idx + token.Length; i++) matched[i] = true;
                from = idx + token.Length;
            }
        }

        var segments = new List<SuggestionTextSegment>();
        int start = 0;
        for (int i = 1; i <= label.Length; i++)
        {
            if (i == label.Length || matched[i] != matched[start])
            {
                segments.Add(new SuggestionTextSegment(label.Substring(start, i - start), matched[start]));
                start = i;
            }
        }
        return segments;
    }

    // ── Selection → navigate → scroll ────────────────────────────────────────

    private void OnSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        // Show the card label in the box as the user arrows through hits; a notice
        // row leaves the text as it was. The TextChanged UserInput guard keeps this
        // programmatic write from re-running the search.
        if (args.SelectedItem is SettingSuggestionHit hit)
            sender.Text = hit.Label;
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // Only a chosen hit navigates. Enter with no selection is a no-op by spec
        // (no standalone results page), and the notice rows navigate nowhere.
        if (args.ChosenSuggestion is SettingSuggestionHit hit)
            NavigateToHit(hit);
    }

    private void NavigateToHit(SettingSuggestionHit hit)
    {
        NavigationViewItem? navItem = FindNavItem(hit.PageTag);
        // A page indexed at boot but withdrawn from the nav since (a module uninstalled
        // while the window is open) has nowhere to go — leave the query for a retry.
        if (navItem is null) return;

        DeckleSettingsSource.Log.SearchNavigated();
        DeckleSettingsSource.Log.SearchNavigatedDetail(hit.PageTag, hit.CardTag);

        // Selecting the item drives OnNavSelectionChanged → Frame.Navigate, which builds
        // the page synchronously. If it is already the selected item on the current page,
        // selection does not change and the page is already built.
        Nav.SelectedItem = navItem;

        if (PageFrame.Content is not FrameworkElement page) return;

        // Clear the query: leaves the box empty and collapses the dropdown. This is a
        // ProgrammaticChange, so the search does not re-run. The overlay, if it carried
        // the query, has done its job — retract it before focus moves to the card.
        SearchBox.Text = string.Empty;
        if (_searchPresentation == SearchPresentation.Overlay)
            SetSearchPresentation(SearchPresentation.Icon, SearchZone.ActualWidth);

        if (page.IsLoaded)
        {
            // Cached page already in the tree (NavigationCacheMode.Required, or already
            // the current page): one dispatcher tick lets any pending layout settle
            // before the scroll. TryEnqueueObserved, not a bare TryEnqueue, per repo rule.
            DispatcherQueue.TryEnqueueObserved(
                operation: "ui-update", caller: "settings-search-scroll",
                callback: () => ScrollToCard(page, hit.CardTag),
                rejectSource: "SETTINGS", rejectWhat: "search scroll-to");
        }
        else
        {
            // Freshly built page: OnNavigatedTo runs before the template is up, so wait
            // the page's first Loaded before walking its tree. One-shot handler.
            void OnPageLoaded(object s, RoutedEventArgs e)
            {
                page.Loaded -= OnPageLoaded;
                ScrollToCard(page, hit.CardTag);
            }
            page.Loaded += OnPageLoaded;
        }
    }

    // Brings the target card into view and focuses it. StartBringIntoView walks up to
    // the page's ScrollViewer on its own, independent of the card's container type. The
    // native focus visual is the only highlight — no custom emphasis, by doctrine.
    private static void ScrollToCard(FrameworkElement page, string cardTag)
    {
        FrameworkElement? card = FindVisualDescendantByTag(page, cardTag);
        if (card is null) return;
        card.StartBringIntoView();
        card.Focus(FocusState.Programmatic);
    }

    private NavigationViewItem? FindNavItem(string pageTag)
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            if (item.Tag as string == pageTag) return item;
        foreach (var item in Nav.FooterMenuItems.OfType<NavigationViewItem>())
            if (item.Tag as string == pageTag) return item;
        return null;
    }

    // Generalises FindVisualDescendantByName (matched on Name) to match on Tag — the
    // identity the composer stamps onto each built card, and the handle a search hit
    // carries. Returns the first FrameworkElement whose Tag string equals the target.
    private static FrameworkElement? FindVisualDescendantByTag(DependencyObject root, string tag)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Tag as string == tag) return fe;
            var found = FindVisualDescendantByTag(child, tag);
            if (found is not null) return found;
        }
        return null;
    }
}
