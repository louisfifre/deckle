using System.Runtime.CompilerServices;
using Deckle.FolderMap;

namespace Deckle.Folders.Views;

// What the self-check drives in the map that the window's own review steps
// do not reach: the "always read" box (12.8c), the boxes per kind and the
// choice between flat and by sections (12.8a). The window keeps its tool
// views to itself, so each map view registers here under the window that
// hosts it; the table holds them weakly, and a closed window takes its view.
public static class MapReview
{
    private static readonly ConditionalWeakTable<IFoldersToolHost, MapView> Views = new();

    internal static void Register(IFoldersToolHost host, MapView view) => Views.AddOrUpdate(host, view);

    // Ticks or unticks the box the way a click does, on the window's map
    // view; nothing happens before the map was first shown in that window.
    public static void SetAlwaysReadOnly(FoldersWindow window, bool alwaysReadOnly)
    {
        if (Views.TryGetValue(window, out MapView? view)) view.SetAlwaysReadOnlyForReview(alwaysReadOnly);
    }

    // Ticks or unticks one kind's box the way a click does.
    public static void SetKindShown(FoldersWindow window, MapItemKind kind, bool shown)
    {
        if (Views.TryGetValue(window, out MapView? view)) view.SetKindShownForReview(kind, shown);
    }

    // Shows the rows by sections (12.8a) or flat (12.8b), the way a click on
    // the display's buttons does.
    public static void SetBySections(FoldersWindow window, bool bySections)
    {
        if (Views.TryGetValue(window, out MapView? view)) view.SetBySectionsForReview(bySections);
    }

    // Opens the large graph (12.9) the way the panel's enlarge button does,
    // or leaves it the way "Quitter le grand graphe" does.
    public static void SetLargeGraph(FoldersWindow window, bool open)
    {
        if (Views.TryGetValue(window, out MapView? view)) view.SetLargeGraphForReview(open);
    }
}
