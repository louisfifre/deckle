using System.Globalization;
using Deckle.Localization;

namespace Deckle.Paste.Views;

// The window's strings read from code, out of this library's own catalog
// (Strings/en-US/Resources.resw).
internal static class PasteText
{
    private const string Library = "Deckle.Paste.Views";

    public static string Get(string key) => Loc.GetFrom(Library, key);

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    // The saver's reasons are English sentences (PasteSaveErrors); the window
    // shows its own resource for each, and the sentence as it is for one it
    // does not know yet.
    public static string ForSaveError(string? error) => error switch
    {
        PasteSaveErrors.NoFolder => Get("Paste_Error_NoFolder"),
        PasteSaveErrors.FolderMissing => Get("Paste_Error_FolderMissing"),
        PasteSaveErrors.NothingToSave => Get("Paste_Error_NothingToSave"),
        PasteSaveErrors.SourceMissing => Get("Paste_Error_SourceMissing"),
        PasteSaveErrors.WriteFailed => Get("Paste_Error_WriteFailed"),
        null or "" => Get("Paste_Error_WriteFailed"),
        _ => error,
    };
}
