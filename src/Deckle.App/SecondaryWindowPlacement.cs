namespace Deckle.App;

// The keys under which the host's own windows keep their place
// (WindowPlacement stores it by key in window-placement.json).
internal static class SecondaryWindowPlacement
{
    public const string Settings = "settings";
    public const string Log = "log";
    public const string TranscribeRewrite = "transcribe-rewrite";
    public const string PasteToFolder = "paste-to-folder";
}
