namespace Deckle.Paste;

// What was pasted since the window opened, newest first: each paste adds its
// rows at the top, in the order it made them. In memory only, emptied when
// the window closes; nothing of it reaches the disk or the log. Bounded by
// Capacity so a window left open all day keeps its newest rows and drops the
// oldest.
public sealed class PasteHistory
{
    public const int Capacity = 100;

    private readonly List<PasteEntry> _entries = [];

    public IReadOnlyList<PasteEntry> Entries => _entries;

    // One paste's rows, put above everything before them.
    public void Add(IReadOnlyList<PasteEntry> paste)
    {
        ArgumentNullException.ThrowIfNull(paste);
        _entries.InsertRange(0, paste);
        if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
    }

    public void Clear() => _entries.Clear();

    // The rows one finished save makes: one per saved file, and above them,
    // when it failed, the row that says why. A copy of several files that
    // stopped part way shows both: the files it left in the folder are there.
    // isFolder tells a copied folder from a file; Directory.Exists by default.
    public static IReadOnlyList<PasteEntry> RowsFor(PasteContent content, PasteSaveResult result, Func<string, bool>? isFolder = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(result);
        isFolder ??= Directory.Exists;

        var rows = new List<PasteEntry>(result.SavedPaths.Count + 1);
        if (!result.Succeeded)
            rows.Add(PasteEntry.Problem(PasteEntryKind.NotSaved, result.Error ?? PasteSaveErrors.WriteFailed));

        foreach (string path in result.SavedPaths)
        {
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            rows.Add(content switch
            {
                PastedImage => PasteEntry.Image(name),
                PastedText text => PasteEntry.Text(name, text.Text),
                _ => PasteEntry.File(name, PasteFileKinds.Of(name, isFolder(path))),
            });
        }
        return rows;
    }
}
