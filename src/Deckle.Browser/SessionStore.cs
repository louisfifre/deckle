using System.Text.Json;

namespace Deckle.Browser;

// Saves and loads a BrowserSession as JSON in a given file. The write goes to
// a temp file then replaces the target, so a crash never leaves half a file.
// A missing or unreadable file is an empty session, never an exception.
public static class SessionStore
{
    private sealed record Entry(string? Url, string? Title);

    // SelectedIndex counts the pinned sites first, then the tabs; -1 is none.
    private sealed record Document(List<Entry>? Pinned, List<Entry>? Tabs, int SelectedIndex);

    public static void Save(BrowserSession session, string path)
    {
        List<BrowserItem> all = [.. session.Pinned, .. session.Tabs];
        var document = new Document(
            [.. session.Pinned.Select(i => new Entry(i.Url, i.Title))],
            [.. session.Tabs.Select(i => new Entry(i.Url, i.Title))],
            all.FindIndex(i => i.Id == session.SelectedId));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(document));
        File.Move(temp, path, overwrite: true);
    }

    public static BrowserSession Load(string path)
    {
        try
        {
            var session = new BrowserSession();
            if (!File.Exists(path)) return session;
            Document? document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (document is null) return session;

            foreach (Entry e in document.Pinned ?? [])
                session.Restore(new BrowserItem(Guid.NewGuid(), e.Url ?? "", e.Title ?? "", true));
            foreach (Entry e in document.Tabs ?? [])
                session.Restore(new BrowserItem(Guid.NewGuid(), e.Url ?? "", e.Title ?? "", false));

            List<BrowserItem> all = [.. session.Pinned, .. session.Tabs];
            if (document.SelectedIndex >= 0 && document.SelectedIndex < all.Count)
                session.RestoreSelection(all[document.SelectedIndex].Id);
            return session;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException or ArgumentException)
        {
            return new BrowserSession();
        }
    }
}
