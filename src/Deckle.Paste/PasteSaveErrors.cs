namespace Deckle.Paste;

// The reasons a paste fails, as the English sentence PasteSaveResult.Error
// carries. A window that localizes its text compares Error with these
// constants and shows its own resource instead; one that does not can show
// Error as it is.
public static class PasteSaveErrors
{
    public const string NoFolder = "Choose a folder first.";
    public const string FolderMissing = "The folder no longer exists. Choose another one.";
    public const string NothingToSave = "The clipboard holds nothing to save.";
    public const string SourceMissing = "A copied file no longer exists.";
    public const string WriteFailed = "The file could not be written in the folder.";

    internal static string For(PasteFailureReason reason) => reason switch
    {
        PasteFailureReason.NoFolder => NoFolder,
        PasteFailureReason.FolderMissing => FolderMissing,
        PasteFailureReason.NothingToSave => NothingToSave,
        PasteFailureReason.SourceMissing => SourceMissing,
        _ => WriteFailed,
    };
}

// The same reasons as the log spells them, one way across every event.
internal enum PasteFailureReason
{
    NoFolder,
    FolderMissing,
    NothingToSave,
    SourceMissing,
    WriteFailed,
    // The window's own: the clipboard could not be read (PasteWindowLog).
    ClipboardUnreadable,
}

// What a paste held, as the log spells it.
internal enum PasteKind
{
    Text,
    Image,
    Files,
}
