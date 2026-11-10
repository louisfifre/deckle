namespace Deckle.Atelier.Views;

// The tools of the window, in the order of their buttons in the top row
// (boards: Fichiers, Photos, Tailles, Carte). One line per tool; the first is
// the one a folder opens in (12.20).
internal static class AtelierToolCatalog
{
    public static IReadOnlyList<IAtelierTool> Create() =>
    [
        new FilesTool(),
        new PhotosTool(),
        new SizesTool(),
        new MapTool(),
    ];
}
