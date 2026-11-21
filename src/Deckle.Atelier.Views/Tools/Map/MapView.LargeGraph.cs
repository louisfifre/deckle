using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Deckle.FolderMap;

namespace Deckle.Atelier.Views;

// The map's large graph (spec, Map; board 12.9). It is not a tool and not a
// tab: the button next to the panel graph's title opens it, "Quitter le grand
// graphe" at the top right and Escape leave it, and nothing else switches
// between the list and the graph (spec, Limits). It takes the place of the
// header and the rows; the panel stays where it is, and the filters stay
// above it with the one state the list reads. A click on an item selects it
// as a click on its row would, so leaving finds the list on that item.
//
// The drawing is behind IMapGraphDrawing: what surrounds it lives here and
// does not change when another engine replaces it.
internal sealed partial class MapView
{
    // Kept for the view's life, as the display is: the large graph stays
    // open from one folder to the next until the person leaves it.
    private bool _largeGraph;
    private IMapGraphDrawing? _drawing;

    private void EnterLargeGraph()
    {
        if (_largeGraph) return;
        _largeGraph = true;
        if (_drawing is null)
        {
            _drawing = new MapColumnGraphDrawing();
            _drawing.ItemChosen += OnGraphItemChosen;
            LargeGraph.Child = _drawing.Element;
        }
        Show();

        // The enlarge button was in the panel, which just lost it: the focus
        // goes to the way out, as the photo seen large does, so Escape
        // reaches the view.
        LeaveLargeGraphButton.Focus(FocusState.Programmatic);
    }

    private void LeaveLargeGraph()
    {
        if (!_largeGraph) return;
        _largeGraph = false;
        Show();

        Rows.UpdateLayout();
        if (Rows.SelectedItem is { } selected && Rows.ContainerFromItem(selected) is ListViewItem row)
            row.Focus(FocusState.Programmatic);
        else
            Rows.Focus(FocusState.Programmatic);
    }

    private void ShowLargeGraph(MapInventory map, MapListFilter filter, MapItem? chosen) =>
        _drawing?.Show(map, filter, chosen);

    // The same selection a click on the item's row makes: the window keeps
    // it, the panel shows the item, and the graph redraws around it.
    private void OnGraphItemChosen(object? sender, MapItem item)
    {
        if (_map is not { } map) return;
        _selection = SelectionOf(map, item);
        Show();
    }

    private void OnLeaveLargeGraphClick(object sender, RoutedEventArgs e) => LeaveLargeGraph();

    // 2D is the only dimension built: a click on it leaves it pressed.
    private void OnTwoDClick(object sender, RoutedEventArgs e) => TwoDButton.IsChecked = true;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape || !_largeGraph) return;
        LeaveLargeGraph();
        e.Handled = true;
    }

    // The self-check's way into the large graph (12.9) and back to the list.
    internal void SetLargeGraphForReview(bool open)
    {
        if (open) EnterLargeGraph();
        else LeaveLargeGraph();
    }
}
