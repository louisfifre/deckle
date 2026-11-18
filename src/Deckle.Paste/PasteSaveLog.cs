using System.Diagnostics;
using Deckle.Diagnostics;

namespace Deckle.Paste;

// How the saver tells the log the end of a paste, the enums spelled by name.
internal static class PasteSaveLog
{
    public static void Saved(PasteKind kind, int items, long bytes, Stopwatch watch)
    {
        string name = kind.ToString();
        DecklePasteSource.Log.PasteSaved(name);
        DecklePasteSource.Log.PasteSavedDetail(name, items, bytes, watch.ElapsedMilliseconds);
    }

    public static void Failed(PasteKind kind, PasteFailureReason reason, Exception? ex, int saved) =>
        DecklePasteSource.Log.PasteSaveFailed(kind.ToString(), reason.ToString(), saved,
            ExceptionTypeName.Of(ex));
}
