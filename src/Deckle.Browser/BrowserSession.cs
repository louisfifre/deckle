namespace Deckle.Browser;

// The pinned sites and the tabs, both ordered, and which one is selected.
// Plain state: no window, no web view. Every change raises Changed once.
public sealed class BrowserSession
{
    private readonly List<BrowserItem> _pinned = [];
    private readonly List<BrowserItem> _tabs = [];

    public IReadOnlyList<BrowserItem> Pinned => _pinned;
    public IReadOnlyList<BrowserItem> Tabs => _tabs;
    public Guid? SelectedId { get; private set; }

    public event EventHandler? Changed;

    public BrowserItem? Selected => Find(SelectedId);

    public BrowserItem? Find(Guid? id) =>
        id is null ? null : _tabs.Concat(_pinned).FirstOrDefault(i => i.Id == id);

    /// <summary>Adds a tab at the end (or at <paramref name="at"/>), selects
    /// it and returns it.</summary>
    public BrowserItem OpenTab(string url, int? at = null)
    {
        var tab = new BrowserItem(Guid.NewGuid(), url, "", false);
        if (at is { } i) _tabs.Insert(Math.Clamp(i, 0, _tabs.Count), tab);
        else _tabs.Add(tab);
        SelectedId = tab.Id;
        RaiseChanged();
        return tab;
    }

    // Used by SessionStore: an item as it was saved, no event, no selection.
    internal void Restore(BrowserItem item) => (item.IsPinned ? _pinned : _tabs).Add(item);

    internal void RestoreSelection(Guid? id) => SelectedId = id;

    /// <summary>Removes a tab. When it was selected, the tab below it takes
    /// the selection, else the one above, else nothing.</summary>
    public void Close(Guid id)
    {
        int index = _tabs.FindIndex(t => t.Id == id);
        if (index < 0) return;
        _tabs.RemoveAt(index);
        if (SelectedId == id)
        {
            SelectedId = index < _tabs.Count ? _tabs[index].Id
                : index > 0 ? _tabs[index - 1].Id
                : null;
        }
        RaiseChanged();
    }

    /// <summary>Moves a tab to <paramref name="index"/> within the tab list.</summary>
    public void Move(Guid id, int index)
    {
        int from = _tabs.FindIndex(t => t.Id == id);
        if (from < 0) return;
        BrowserItem tab = _tabs[from];
        _tabs.RemoveAt(from);
        _tabs.Insert(Math.Clamp(index, 0, _tabs.Count), tab);
        RaiseChanged();
    }

    /// <summary>The tab becomes a pinned site at the end of the pinned list.</summary>
    public void Pin(Guid tabId)
    {
        BrowserItem? tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null) return;
        _tabs.Remove(tab);
        tab.IsPinned = true;
        _pinned.Add(tab);
        RaiseChanged();
    }

    /// <summary>The pinned site becomes a tab at the top of the tab list.</summary>
    public void Unpin(Guid id)
    {
        BrowserItem? site = _pinned.FirstOrDefault(t => t.Id == id);
        if (site is null) return;
        _pinned.Remove(site);
        site.IsPinned = false;
        _tabs.Insert(0, site);
        RaiseChanged();
    }

    public void Select(Guid id)
    {
        if (Find(id) is null || SelectedId == id) return;
        SelectedId = id;
        RaiseChanged();
    }

    /// <summary>Tells the session an item's url or title changed.</summary>
    public void Touch() => RaiseChanged();

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
