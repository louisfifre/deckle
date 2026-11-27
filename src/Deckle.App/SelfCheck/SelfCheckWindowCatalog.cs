using Deckle.Hosting.SelfCheck;

namespace Deckle.App.SelfCheck;

// The windows the published Deckle's full run walks, in walk order. The
// shared harness takes the list; it names no window. Deckle Projects is walked
// by its own app (DeckleProjects.exe --self-check).
internal static class SelfCheckWindowCatalog
{
    public static readonly SelfCheckWindows Published = new(
        Windows:
            ["settings", "log", "transcribe-rewrite", "paste-to-folder"],
        // The windows whose walk shows more than one view.
        WithViews:
            ["settings", "transcribe-rewrite", "paste-to-folder"]);
}
