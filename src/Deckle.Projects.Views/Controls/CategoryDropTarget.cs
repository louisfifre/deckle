using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Windows.ApplicationModel.DataTransfer;

namespace Deckle.Projects.Views;

// Makes a section accept a dragged project: the whole section is the target,
// not only the words in it (spec: a card dragged onto a category joins it,
// dragged onto "Without a category" it leaves its category).
//
// The target says so while a project is over it: the standard caption of the
// drag ("Move to Development", the system's own DragUIOverride) and a frame
// round the section, a subtle fill with a 2 DIP accent border. The caption is
// small and follows the cursor, while a section is a large area of a scrolling
// page, so the frame tells which one will take the card, as File Explorer's
// folders do. A project already in the section is refused instead: no frame, no
// caption, and the cursor shows the system's "not allowed", as Explorer does for
// a drop onto the folder an item already sits in.
//
// The frame is drawn behind the content and reaches 8 DIP past it, without
// taking any room in the layout.
internal static class CategoryDropTarget
{
    private const string Namespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    // Wraps the content in the target and returns it, to be the section's
    // content. categoryName is what the caption names, read each time because
    // a kept section follows its category's renaming. alreadyThere says whether
    // the project (by id) already sits in this section. onDrop gets the id of
    // the project dropped.
    public static FrameworkElement Attach(
        UIElement content, Func<string> categoryName, Func<string, bool> alreadyThere, Action<string> onDrop)
    {
        // Parsed from XAML so its brushes are {ThemeResource}s that follow the
        // window's theme (see Themed).
        var frame = (Border)XamlReader.Load(
            $"<Border xmlns=\"{Namespace}\" Margin=\"-8\" BorderThickness=\"2\" Visibility=\"Collapsed\" IsHitTestVisible=\"False\" "
            + "Background=\"{ThemeResource SubtleFillColorSecondaryBrush}\" BorderBrush=\"{ThemeResource AccentFillColorDefaultBrush}\" "
            + "CornerRadius=\"{ThemeResource OverlayCornerRadius}\" />");

        // A background, even a clear one, is what the pointer hits while
        // dragging over the gaps between the cards.
        var target = new Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            AllowDrop = true,
            Children = { frame, content },
        };

        void Show(bool shown) => frame.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        target.DragEnter += (_, e) => Show(ProjectDragData.IsProject(e.DataView) && !IsNoOp(e.DataView));

        target.DragOver += (_, e) =>
        {
            if (!ProjectDragData.IsProject(e.DataView)) return;
            if (IsNoOp(e.DataView))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                Show(false);
                return;
            }
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = ProjectsText.Format("Home_DropCaption_Format", categoryName());
            e.DragUIOverride.IsGlyphVisible = false;
            e.DragUIOverride.IsContentVisible = true;
            Show(true);
        };

        // DragLeave also reaches the target from the elements inside it (the
        // pointer going from one card to the next fires it), so it is only a
        // real leave when the event comes from the target itself or the pointer
        // is outside its bounds. Hiding on every one made the frame flicker
        // between cards. The target's own event is kept as a leave so the frame
        // cannot stay stuck when a drag ends with the position still inside;
        // DragOver draws it again at the next move.
        target.DragLeave += (_, e) =>
        {
            Windows.Foundation.Point at = e.GetPosition(target);
            bool outside = at.X < 0 || at.Y < 0 || at.X >= target.ActualWidth || at.Y >= target.ActualHeight;
            if (ReferenceEquals(e.OriginalSource, target) || outside) Show(false);
        };

        target.Drop += async (_, e) =>
        {
            Show(false);
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

        // The id travels in the package's properties as well, which can be read
        // while the drag is still over the target (the data itself is read
        // asynchronously, at the drop).
        bool IsNoOp(DataPackageView data) => ProjectDragData.PeekId(data) is { } id && alreadyThere(id);

        return target;
    }
}
