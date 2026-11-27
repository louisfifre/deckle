using Deckle.Folders;
using Deckle.Folders.Views;

namespace Deckle.Folders.App;

// The self-check's walk of the window at the widths its layout changes at
// (FoldersWindow.Widths.cs), called from WalkFoldersAsync once the address
// field's forms are photographed, so the window stands in the sample's
// photo folder. Each state shows that folder with a photo selected and the
// Photos tool active: at 960 with the pane as the rail, then at 640, with the
// pane laid over the page, and with the details panel pressed open in the
// narrow window. The window goes back to the size and the tool the walk had.
internal static partial class FoldersSelfCheck
{
    private static async Task WalkFoldersWidthsAsync(
        FoldersWindow window, string sample, Func<string, Func<Task>?, Task> capture)
    {
        await capture("width-960", async () =>
        {
            // Back in the folder, the address field at rest, as the text form
            // the walk left it in would hide the current-folder form.
            await window.OpenForReviewAsync("..");
            await window.OpenForReviewAsync("2026-06-14 Mariage");
            await window.ShowToolForReviewAsync("photos");
            await window.SelectForReviewAsync("DSC_0207.JPG");
            window.SetPaneOpenForReview(false);
            ResizeFolders(window, 960, FoldersDrawnHeight);
            await Task.Delay(400);
        });
        await capture("width-640", async () =>
        {
            ResizeFolders(window, 640, FoldersDrawnHeight);
            await Task.Delay(400);
        });
        await capture("width-640-pane", async () =>
        {
            window.SetPaneOpenForReview(true);
            await Task.Delay(400);
        });
        window.SetPaneOpenForReview(false);
        await capture("width-640-details", async () =>
        {
            window.SetDetailsOnForReview(true);
            await Task.Delay(400);
        });
        window.SetDetailsOnForReview(false);

        // Back to the walk's size, with the panel as the wide window had it.
        ResizeFolders(window, FoldersDrawnWidth, FoldersDrawnHeight);
        await Task.Delay(400);
        await window.ShowToolForReviewAsync("files");
    }
}
