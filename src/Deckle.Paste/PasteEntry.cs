namespace Deckle.Paste;

// One row of the window's list of what was pasted since it opened: a saved
// image, a saved text with the beginning of its words, one saved file, or a
// problem with its reason. It lives in memory only (PasteHistory): nothing of
// it is written to disk or to the log.
//
// Name is the saved file's name for a saved row, empty for a problem. Detail
// is the text's excerpt for a text, the reason sentence for a problem (a
// PasteSaveErrors sentence or one the window gave), null otherwise.
public sealed record PasteEntry(PasteEntryKind Kind, string Name, string? Detail, PasteFileKind FileKind)
{
    public bool IsProblem => Kind is PasteEntryKind.NotSaved
        or PasteEntryKind.FolderNotChanged or PasteEntryKind.FolderNotRemembered;

    public static PasteEntry Image(string name) =>
        new(PasteEntryKind.Image, name, null, PasteFileKind.Image);

    public static PasteEntry Text(string name, string text) =>
        new(PasteEntryKind.Text, name, PasteExcerpt.Of(text), PasteFileKind.Document);

    public static PasteEntry File(string name, PasteFileKind kind) =>
        new(PasteEntryKind.File, name, null, kind);

    public static PasteEntry Problem(PasteEntryKind kind, string reason)
    {
        var entry = new PasteEntry(kind, "", reason, PasteFileKind.Other);
        if (!entry.IsProblem)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a problem kind.");
        return entry;
    }
}

public enum PasteEntryKind
{
    // A clipboard image saved as a PNG.
    Image,
    // A clipboard text saved as a .txt.
    Text,
    // A file or folder copied in Explorer, saved under its own name.
    File,
    // A paste that saved nothing (or stopped part way).
    NotSaved,
    // The folder picker did not open: the folder stays as it was.
    FolderNotChanged,
    // A folder chosen for this run that could not be remembered.
    FolderNotRemembered,
}

// What a saved file is, for the icon its row shows.
public enum PasteFileKind
{
    Other,
    Image,
    Video,
    Audio,
    Document,
    Folder,
}
