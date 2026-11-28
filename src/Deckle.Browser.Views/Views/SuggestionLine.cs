namespace Deckle.Browser.Views;

/// <summary>What a suggestion of the address field shows: the title (the domain when the page had none) and the
/// domain, and the address it goes to. Memory only, never logged.</summary>
public sealed class SuggestionLine(Suggestion suggestion)
{
    public string Title { get; } = suggestion.Title.Length > 0 ? suggestion.Title : suggestion.Domain;
    public string Domain { get; } = suggestion.Domain;
    public string Url { get; } = suggestion.Url;
}
