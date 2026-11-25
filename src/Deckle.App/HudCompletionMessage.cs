using Deckle.Transcription;

namespace Deckle.App;

// What the HUD says once a take is over, from what the engine delivered.
internal enum HudCompletionMessage { Hidden, Copied, FileSaved }

internal static class HudCompletion
{
    public static HudCompletionMessage For(TranscriptionOutcome outcome) => outcome switch
    {
        TranscriptionOutcome.ClipboardOnly => HudCompletionMessage.Copied,
        TranscriptionOutcome.SavedToFile => HudCompletionMessage.FileSaved,
        _ => HudCompletionMessage.Hidden,
    };
}
