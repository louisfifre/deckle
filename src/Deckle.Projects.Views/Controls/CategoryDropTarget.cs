using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace Deckle.Projects.Views;

// Makes an element accept a dragged project: the whole element is the target,
// not only the words in it (spec: a card dragged onto a category joins it,
// dragged onto "Without a category" it leaves its category).
//
// The standard caption of the drag ("Move to Development") is the system's own
// DragUIOverride, so the cursor says what the drop will do without a hand-drawn
// highlight. The element needs a background to be hit by the pointer, even a
// transparent one.
internal static class CategoryDropTarget
{
    // categoryName is what the caption names. onDrop gets the id of the project
    // dropped; whether it already sits in the target is the caller's rule.
    public static void Attach(UIElement target, string categoryName, Action<string> onDrop)
    {
        target.AllowDrop = true;

        target.DragOver += (_, e) =>
        {
            if (!ProjectDragData.IsProject(e.DataView)) return;
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = ProjectsText.Format("Home_DropCaption_Format", categoryName);
            e.DragUIOverride.IsGlyphVisible = false;
            e.DragUIOverride.IsContentVisible = true;
        };

        target.Drop += async (_, e) =>
        {
            // Taking the data awaits, and a deferral keeps the drop open until
            // it is read.
            DragOperationDeferral deferral = e.GetDeferral();
            try
            {
                string? projectId = await ProjectDragData.TakeAsync(e.DataView);
                if (projectId is not null) onDrop(projectId);
            }
            finally
            {
                deferral.Complete();
            }
        };
    }
}
