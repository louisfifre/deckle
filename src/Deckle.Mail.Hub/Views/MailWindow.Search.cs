using Microsoft.UI.Xaml.Controls;

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
public sealed partial class MailWindow
{
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
            MailDates.Day(hit.Date, Session.Today)))];
    }

    private string WhereOf(MailSearchPlace place)
        => place.Gesture is { } gesture ? MailText.Result(gesture, Session.Today)
            : place.Reason is { } reason ? MailText.Reason(reason)
            : MailText.Get("Search_InTriage");

}

// One line of the search menu. SubjectId is null for the notice that nothing
// matches, which opens nothing.
public sealed class SearchLine(string? subjectId, string sentence, string language, string where, string date)
{
    public string? SubjectId { get; } = subjectId;
    public string Sentence { get; } = sentence;
    public string Language { get; } = language;
    public string Where { get; } = where;
    public string Date { get; } = date;

    public static SearchLine Notice(string text)
        => new(null, text, "en-US", "", "");

    // Narrator reads the line, not the type name.
    public override string ToString() => SubjectId is null ? Sentence : $"{Sentence}, {Where}, {Date}";
}
