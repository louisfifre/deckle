using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation.Collections;

namespace Deckle.Projects.Views;

// Changing the order of the projects in the category (spec: "dragging a row by
// its gripper changes the order; Home shows them in that order").
//
// A ListView with CanReorderItems moves its items itself while the row is
// dragged, and starts the drag from anywhere on the row. Two things are added:
//
//   the handle   the drag is cancelled unless the press landed on the dotted
//                handle, so a press on the path or the name keeps its meaning
//                (a click opens the project)
//   the commit   the new order is written to the store once the row is let go
//                (or, for the keyboard's Alt+Shift+Up/Down, when the list
//                changes with no drag going on), never while it is still moving
//   the voice    a keyboard move is announced to a screen reader with the row's
//                new position; a drag is not, the person sees where it lands
internal sealed partial class CategoryPage
{
    private bool _dragging;

    private void Wire()
    {
        // The keyboard moves a row without a drag; the list then changes while
        // _dragging is false and Build is not the one changing it.
        Rows.Items.VectorChanged += OnItemsChanged;
    }

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // Only a press on the handle drags. The press that began this drag is
        // the row's last one, so its flag says where it landed.
        if (e.Items.OfType<CategoryRow>().Any(row => !row.GripPressed))
        {
            e.Cancel = true;
            return;
        }
        _dragging = true;
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _dragging = false;
        CommitOrder();
    }

    private void OnItemsChanged(IObservableVector<object> sender, IVectorChangedEventArgs args)
    {
        if (_building || _dragging) return;
        if (args.CollectionChange == CollectionChange.ItemInserted
            && args.Index < Rows.Items.Count
            && Rows.Items[(int)args.Index] is CategoryRow moved)
            AnnounceMove(moved, (int)args.Index);
        CommitOrder();
    }

    // The keyboard moves a row with no sound of its own, so the new place is said
    // aloud: "Name, position 2 of 4". It is raised from the list's peer, which
    // outlives the redraw the store's change causes right after (the row's own
    // peer goes with the row).
    private void AnnounceMove(CategoryRow row, int index)
    {
        string name = AutomationProperties.GetName(row);
        string text = ProjectsText.Format("Category_Moved_Format", name, index + 1, Rows.Items.Count);
        FrameworkElementAutomationPeer.FromElement(Rows)?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            text,
            "ProjectsRowMoved");
    }

    // Writes the list's order to the store when it differs from the store's.
    // The store raises Changed, and the page is drawn again from it, later.
    private void CommitOrder()
    {
        List<string> ordered = [.. RowControls().Select(r => r.ProjectId)];
        if (ordered.SequenceEqual(ProjectsHere().Select(p => p.Id))) return;
        _context.Store.ReorderInCategory(_categoryId, ordered);
    }
}
