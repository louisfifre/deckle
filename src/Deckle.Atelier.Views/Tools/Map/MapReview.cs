using System.Runtime.CompilerServices;
using Deckle.FolderMap;

namespace Deckle.Atelier.Views;

// What the self-check drives in the map that the window's own review steps
// do not reach: the "always read" box (12.8c), the boxes per kind and the
// choice between flat and by sections (12.8a). The window keeps its tool
// views to itself, so each map view registers here under the window that
// hosts it; the table holds them weakly, and a closed window takes its view.
public static class MapReview
{
    private static readonly ConditionalWeakTable<IAtelierToolHost, MapView> Views = new();

    internal static void Register(IAtelierToolHost host, MapView view) => Views.AddOrUpdate(host, view);

    // Ticks or unticks the box the way a click does, on the window's map
    // view; nothing happens before the map was first shown in that window.
    public static void SetAlwaysReadOnly(AtelierWindow window, bool alwaysReadOnly)
    {
        if (Views.TryGetValue(window, out MapView? view)) view.SetAlwaysReadOnlyForReview(alwaysReadOnly);
    }

    // Ticks or unticks one kind's box the way a click does.
    public static void SetKindShown(AtelierWindow window, MapItemKind kind, bool shown)
    {
        if (Views.TryGetValue(window, out MapView? view)) view.SetKindShownForReview(kind, shown);
    }

    // Shows the rows by sections (12.8a) or flat (12.8b), the way a click on
    // the display's buttons does.
    public static void SetBySections(AtelierWindow window, bool bySections)
    {
        if (Views.TryGetValue(window, out MapView? view)) view.SetBySectionsForReview(bySections);
    }

    // Opens the large graph (12.9) the way the panel's enlarge button does,
    // or leaves it the way "Quitter le grand graphe" does.
    public static void SetLargeGraph(AtelierWindow window, bool open)
    {
        if (Views.TryGetValue(window, out MapView? view)) view.SetLargeGraphForReview(open);
    }
}
