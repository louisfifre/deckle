using System.Runtime.CompilerServices;

namespace Deckle.Atelier.Views;

// What the self-check drives in the map that the window's own review steps
// do not reach: the "always read" box (12.8c). The window keeps its tool
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
}
