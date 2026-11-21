using Microsoft.UI.Xaml;
using Deckle.FolderMap;

namespace Deckle.Folders.Views;

// The drawing inside the map's large graph, apart from the surface around it
// (the filters, the 2D/3D choice, "Quitter le grand graphe"), which MapView
// owns. Louis said on 2026-11-15 that the drawing itself is decided later:
// another engine replaces MapColumnGraphDrawing by implementing this, and
// nothing around it changes.
internal interface IMapGraphDrawing
{
    // What the surface puts in the centre; it fills the room it is given.
    FrameworkElement Element { get; }

    // The person clicked an item, or pressed Enter or Space on it: the
    // surface selects it and shows it in the panel, then calls Show again.
    event EventHandler<MapItem>? ItemChosen;

    // Draws the items the filter keeps and the links between them, the
    // chosen item and its neighbours standing out. Called again whenever the
    // inventory, the filter or the choice changes; a drawing that had the
    // keyboard focus keeps it on the chosen item.
    void Show(MapInventory map, MapListFilter filter, MapItem? chosen);
}
