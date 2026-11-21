using Deckle.Hosting.SelfCheck;

namespace Deckle.App.SelfCheck;

// The windows the published Deckle's full run walks, in walk order: its own,
// then the contributed ones (PublishedComposition) before Projects, in
// composition order. The shared harness takes the list; it names no window.
internal static class SelfCheckWindowCatalog
{
    public static readonly SelfCheckWindows Published = new(
        Windows:
        [
            "settings", "log", "transcribe-rewrite", "paste-to-folder",
            .. PublishedComposition.Walks.Select(w => w.WindowName),
            "projects",
        ],
        // The windows whose walk shows more than one view.
        WithViews:
        [
            "settings", "transcribe-rewrite", "paste-to-folder",
            .. PublishedComposition.Walks.Where(w => w.HasViews).Select(w => w.WindowName),
            "projects",
        ]);
}
