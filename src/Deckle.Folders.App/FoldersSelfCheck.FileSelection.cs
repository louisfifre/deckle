using Deckle.Folders;
using Deckle.Folders.Views;

namespace Deckle.Folders.App;

// The self-check's walk of the file tool's states no board draws (spec, Open
// 33), called from WalkFoldersAsync once the three displays are photographed:
// several items selected with the panel of the group, in each display, which
// keeps the selection; the sample's very long name selected, in each display,
// its tooltip checked against its ellipsis since no image keeps a popup open; and the sample's
// empty folder; then the panel hidden by its toggle, and shown with nothing
// selected. Nothing is opened nor sent to the Recycle Bin. It ends where
// it began: in the root of the sample, in the list, "notes-2023.zip"
// selected, so the states after it see what they saw before.
internal static partial class FoldersSelfCheck
{
    private static async Task WalkFoldersFileSelectionAsync(
        FoldersWindow window, Func<string, Func<Task>?, Task> capture)
    {
        // A folder and two files: the panel counts the folder and sums the
        // files alone.
        await capture("files-several-selected",
            () => FilesReview.SelectAsync(window, "Documents", "Tarifs-2026.pdf", "notes-2023.zip"));
        await capture("files-several-selected-large-rows",
            () => FilesReview.ShowDisplayAsync(window, FileListDisplay.LargeRows));
        await capture("files-several-selected-gallery",
            () => FilesReview.ShowDisplayAsync(window, FileListDisplay.Gallery));

        async Task LongName(FileListDisplay display)
        {
            await FilesReview.ShowDisplayAsync(window, display);
            await window.SelectForReviewAsync(SampleFolder.LongFileName);
            await FilesReview.RequireNameToolTipAsync(window, SampleFolder.LongFileName);
        }
        await capture("files-long-name-gallery", () => LongName(FileListDisplay.Gallery));
        await capture("files-long-name-large-rows", () => LongName(FileListDisplay.LargeRows));
        await capture("files-long-name", () => LongName(FileListDisplay.List));

        await capture("files-empty-folder", () => window.OpenForReviewAsync(SampleFolder.EmptyFolderName));
        await window.OpenForReviewAsync("..");
        await window.SelectForReviewAsync("notes-2023.zip");

        // The panel's two other states: hidden by the toggle (the centre
        // takes the width), and shown with nothing selected (its one line).
        await capture("details-hidden", () =>
        {
            window.SetDetailsOnForReview(false);
            return Task.CompletedTask;
        });
        await capture("details-nothing-selected", async () =>
        {
            window.SetDetailsOnForReview(true);
            await window.ClearSelectionForReviewAsync();
        });
        await window.SelectForReviewAsync("notes-2023.zip");
    }
}
