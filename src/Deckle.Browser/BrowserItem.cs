namespace Deckle.Browser;

// One page the panel lists, pinned or open as a tab. It keeps its Id when it
// is pinned or unpinned, so the view that shows it keeps its web view.
public sealed class BrowserItem(Guid id, string url, string title, bool isPinned)
{
    public Guid Id { get; } = id;
    public string Url { get; set; } = url;
    public string Title { get; set; } = title;
    public bool IsPinned { get; internal set; } = isPinned;
}
