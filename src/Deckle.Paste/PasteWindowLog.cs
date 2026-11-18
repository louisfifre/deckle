using Deckle.Diagnostics;

namespace Deckle.Paste;

// How the window tells the log the work that stops before the saver: a paste
// whose clipboard gave nothing to save, a folder picker that did not open.
// The reasons are spelled by PasteFailureReason, as the saver's are; nothing
// of what the clipboard held is written.
public static class PasteWindowLog
{
    public static void ClipboardUnreadable(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        DecklePasteSource.Log.PasteReadFailed(
            PasteFailureReason.ClipboardUnreadable.ToString(), ExceptionTypeName.Of(ex), ex.HResult);
    }

    public static void NothingToSave() =>
        DecklePasteSource.Log.PasteReadFailed(PasteFailureReason.NothingToSave.ToString(), "", 0);

    public static void FolderPickerFailed(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        DecklePasteSource.Log.FolderPickerFailed(ExceptionTypeName.Of(ex), ex.HResult);
    }
}
