using System.Text.Json;

namespace Deckle.Browser;

/// <summary>One page visited: its address, the title it had, when it was last
/// visited (UTC) and how many times.</summary>
public sealed record HistoryEntry(string Url, string Title, DateTimeOffset LastVisit, int Visits);

/// <summary>A suggestion for the address field: what a line shows (the title
/// and the domain) and where choosing it goes.</summary>
public sealed record Suggestion(string Url, string Title, string Domain);

// The pages visited, kept for the address field's suggestions. Plain state:
// no window, no web view, and nothing in it is ever sent anywhere. An entry is
// recorded per successful top-level navigation of an http or https page; the
// oldest are dropped beyond Capacity. Saved as JSON by HistoryStore.
public sealed class History
{
    public const int Capacity = 5000;

    private readonly Dictionary<string, HistoryEntry> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public IReadOnlyCollection<HistoryEntry> Entries => _entries.Values;

    public event EventHandler? Changed;

    /// <summary>Only http and https pages are kept: nothing else (a blank page,
    /// a file, a page of the browser) is a visit.</summary>
    public static bool IsRecordable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>A successful navigation to the address: a new entry, or one
    /// more visit of the entry it already has. False when the address is not
    /// recordable.</summary>
    public bool Record(string url, string title, DateTimeOffset at)
    {
        if (!IsRecordable(url)) return false;
        string key = new Uri(url).AbsoluteUri;
        _entries[key] = _entries.TryGetValue(key, out HistoryEntry? known)
            ? known with { Title = title.Length > 0 ? title : known.Title, LastVisit = at, Visits = known.Visits + 1 }
            : new HistoryEntry(key, title, at, 1);
        Trim();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The page's title arrived or changed after its navigation: the
    /// entry takes it, without counting a visit.</summary>
    public void Retitle(string url, string title)
    {
        if (title.Length == 0 || !IsRecordable(url)) return;
        string key = new Uri(url).AbsoluteUri;
        if (!_entries.TryGetValue(key, out HistoryEntry? known) || known.Title == title) return;
        _entries[key] = known with { Title = title };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Used by HistoryStore: an entry as it was saved, no event.
    internal void Restore(HistoryEntry entry)
    {
        if (!IsRecordable(entry.Url)) return;
        _entries[entry.Url] = entry;
        Trim();
    }

    private void Trim()
    {
        if (_entries.Count <= Capacity) return;
        foreach (HistoryEntry oldest in _entries.Values.OrderBy(e => e.LastVisit).Take(_entries.Count - Capacity).ToList())
            _entries.Remove(oldest.Url);
    }

    /// <summary>The entries whose host starts with the typed text (a leading
    /// "www." ignored on both) or whose address or title contains it, ignoring
    /// case, the most visited first and, among equals, the most recent. Nothing
    /// typed gives nothing.</summary>
    public IReadOnlyList<Suggestion> Suggest(string typed, int max = 6)
    {
        string text = typed.Trim();
        if (text.Length == 0 || max <= 0) return [];
        string hostText = WithoutWww(text);

        return
        [
            .. _entries.Values
                .Where(e => Matches(e, text, hostText))
                .OrderByDescending(e => e.Visits)
                .ThenByDescending(e => e.LastVisit)
                .Take(max)
                .Select(e => new Suggestion(e.Url, e.Title, DomainOf(e.Url))),
        ];
    }

    private static bool Matches(HistoryEntry entry, string text, string hostText)
    {
        var uri = new Uri(entry.Url);
        if (WithoutWww(uri.IdnHost).StartsWith(hostText, StringComparison.OrdinalIgnoreCase)) return true;
        return entry.Url.Contains(text, StringComparison.OrdinalIgnoreCase)
            || entry.Title.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithoutWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    private static string DomainOf(string url) => SiteName.Display(new Uri(url));
}

// Saves and loads a History as JSON in a given file, as SessionStore does: the
// write goes to a temp file then replaces the target, and a missing or
// unreadable file is an empty history, never an exception.
public static class HistoryStore
{
    private sealed record Entry(string? Url, string? Title, DateTimeOffset LastVisit, int Visits);

    public static void Save(History history, string path)
    {
        List<Entry> entries = [.. history.Entries.Select(e => new Entry(e.Url, e.Title, e.LastVisit, e.Visits))];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries));
        File.Move(temp, path, overwrite: true);
    }

    public static History Load(string path)
    {
        var history = new History();
        try
        {
            if (!File.Exists(path)) return history;
            foreach (Entry e in JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? [])
            {
                if (e.Url is null) continue;
                history.Restore(new HistoryEntry(e.Url, e.Title ?? "", e.LastVisit, Math.Max(1, e.Visits)));
            }
            return history;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException or ArgumentException)
        {
            return new History();
        }
    }
}
