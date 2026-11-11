using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Mail.Hub;

// ── The search of the title bar ───────────────────────────────────────────────
//
// It answers in the menu under the field as the person types; choosing a line
// opens its subject. No page lists results (spec, The window). Matching and
// the place of each subject are MailSearch's.
//
// Only QuerySubmitted opens a subject. SuggestionChosen also fires while the
// person arrows through the menu (Learn, AutoSuggestBox: it exists to fill the
// box as the selection moves), so navigating there opened the first line on
// the first Down. UpdateTextOnSelect is off in the XAML: arrowing never
// replaces what the person typed, and the box is emptied only once a subject
// actually opens. The "no mail matches" line opens nothing and keeps the text.
//
// In a narrow window the field becomes a button (spec, A narrow window). The
// measure is the zone the title bar gives the field, not the window's width:
// the zone is the bar's one stretching column, so its width is the room the
// bar really has, whatever the title, the scale and the problems button take.
// Under SearchIconZoneWidth the field leaves for the button; a click on the
// button stands the same field in SearchOverlay, under the bar, until it
// loses the focus; above SearchInlineZoneWidth it goes back into the bar. The
// 24 px between the two widths keep the swap from flickering on a boundary.
// This is SettingsWindow.Search.cs's pattern, with its widths.
public sealed partial class MailWindow
{
    private const double SearchIconZoneWidth = 180.0;
    private const double SearchInlineZoneWidth = 204.0;

    private enum SearchPresentation { Inline, Icon, Overlay }
    private SearchPresentation _searchPresentation = SearchPresentation.Inline;

    private void OnSearchZoneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double zone = e.NewSize.Width;
        if (_searchPresentation == SearchPresentation.Inline && zone < SearchIconZoneWidth)
            SetSearchPresentation(SearchPresentation.Icon);
        else if (_searchPresentation != SearchPresentation.Inline && zone >= SearchInlineZoneWidth)
            SetSearchPresentation(SearchPresentation.Inline);
    }

    private void OnSearchIconClick(object sender, RoutedEventArgs e)
    {
        SetSearchPresentation(SearchPresentation.Overlay);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void SetSearchPresentation(SearchPresentation target)
    {
        if (target == _searchPresentation) return;
        _searchPresentation = target;

        // The field lives in one of two hosts, the bar's zone or the overlay:
        // move it first, then show what the state shows.
        if (target == SearchPresentation.Overlay)
        {
            SearchZone.Children.Remove(SearchBox);
            SearchOverlay.Child = SearchBox;
            SearchBox.Visibility = Visibility.Visible;
            SearchOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            if (ReferenceEquals(SearchOverlay.Child, SearchBox))
            {
                SearchOverlay.Child = null;
                SearchZone.Children.Insert(0, SearchBox);
            }
            SearchOverlay.Visibility = Visibility.Collapsed;
            SearchBox.Visibility = target == SearchPresentation.Inline ? Visibility.Visible : Visibility.Collapsed;
        }
        SearchIconButton.Visibility = target == SearchPresentation.Inline ? Visibility.Collapsed : Visibility.Visible;
    }

    // The overlay closes when the field gives up the focus: Escape once its
    // menu is closed, a press anywhere else, or a subject opening.
    private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape || SearchBox.IsSuggestionListOpen) return;
        if (_searchPresentation != SearchPresentation.Overlay) return;
        SetSearchPresentation(SearchPresentation.Icon);
        PageFrame.Focus(FocusState.Programmatic);
        e.Handled = true;
    }

    private void OnSearchBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_searchPresentation != SearchPresentation.Overlay) return;
        // The focus passes through the menu while a line is chosen: decide on
        // where it has settled, one turn of the dispatcher later.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_searchPresentation != SearchPresentation.Overlay) return;
            if (IsFocusInSearch() || SearchBox.IsSuggestionListOpen) return;
            SetSearchPresentation(SearchPresentation.Icon);
        });
    }

    // A press outside the field, on a page or the rail, which take no focus
    // of their own: the overlay still closes.
    private void CloseSearchOverlayOnPressOutside(PointerRoutedEventArgs e)
    {
        if (_searchPresentation != SearchPresentation.Overlay) return;
        if (e.OriginalSource is DependencyObject source
            && (IsWithin(source, SearchZone) || IsWithin(source, SearchOverlay))) return;
        SetSearchPresentation(SearchPresentation.Icon);
    }

    private bool IsFocusInSearch()
        => Content?.XamlRoot is { } root
           && FocusManager.GetFocusedElement(root) is DependencyObject focused
           && (IsWithin(focused, SearchZone) || IsWithin(focused, SearchOverlay));

    private static bool IsWithin(DependencyObject node, DependencyObject root)
    {
        for (DependencyObject? d = node; d is not null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, root)) return true;
        return false;
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        sender.ItemsSource = SearchLines(sender.Text);
    }


    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SearchLine { SubjectId: { } chosen })
        {
            sender.Text = "";
            Go(MailDestination.Subject(chosen));
            return;
        }
        // Enter without a chosen line opens the first one.
        if (SearchLines(args.QueryText).FirstOrDefault() is { SubjectId: { } first })
        {
            sender.Text = "";
            Go(MailDestination.Subject(first));
        }
    }

    private List<SearchLine> SearchLines(string text)
    {
        if (text.Trim().Length < 2) return [];
        IReadOnlyList<MailSearchHit> hits = MailSearch.Find(Session, text);
        if (hits.Count == 0)
            return [SearchLine.Notice(MailText.Get("Search_NoMatch"))];

        return [.. hits.Select(hit => new SearchLine(
            hit.SubjectId,
            hit.Sentence,
            hit.Language,
            WhereOf(hit.Place),
            MailDates.Day(hit.Date, Session.Today),
            whereIsState: hit.Place.Gesture is null && hit.Place.Reason is not null))];
    }

    private string WhereOf(MailSearchPlace place)
        => place.Gesture is { } gesture ? MailText.Result(gesture, Session.Today)
            : place.Reason is { } reason ? MailText.Reason(reason)
            : MailText.Get("Search_InTriage");

}

// One line of the search menu. SubjectId is null for the notice that nothing
// matches, which opens nothing. Where a subject stands is either plain grey
// words (In Triage, the result of a gesture) or, when the sorting set it
// aside, the state the sorting set, which is a framed label like everywhere
// else (spec: a state the sorting sets on a row is a framed label, never
// plain text).
public sealed class SearchLine(string? subjectId, string sentence, string language, string where, string date, bool whereIsState = false)
{
    public string? SubjectId { get; } = subjectId;
    public string Sentence { get; } = sentence;
    public string Language { get; } = language;
    public string Where { get; } = where;
    public string Date { get; } = date;
    public Microsoft.UI.Xaml.Visibility WhereAsWords => whereIsState ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public Microsoft.UI.Xaml.Visibility WhereAsLabel => whereIsState ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public static SearchLine Notice(string text)
        => new(null, text, "en-US", "", "");

    // Narrator reads the line, not the type name.
    public override string ToString() => SubjectId is null ? Sentence : MailText.Format("Join_Comma3_Format", Sentence, Where, Date);
}
