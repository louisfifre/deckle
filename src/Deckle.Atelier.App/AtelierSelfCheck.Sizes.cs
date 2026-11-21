using Deckle.Atelier;
using Deckle.Atelier.Views;
using Deckle.Core;

namespace Deckle.Atelier.App;

// The self-check's walk of the sizes tool, called from WalkAtelierAsync once
// every tool has been photographed. It adds to the sample folder what the
// sizes need (SizesSample: sparse files, so nothing weighs on the disk), then
// photographs the analysis finished, one of the heaviest files selected (12.21),
// and one folder down, which re-roots the report instead of reading the disk
// again.
//
// Then the states no board draws but the spec names (12.23, Open 33): an
// analysis held midway by a gate on two folders of the sample, so some rows
// show their size and the others "…", with the progress bar and Stop; the same
// analysis stopped; Refresh on it, which must measure again; an empty folder;
// a folder the user may not list, made for that one image and removed after.
// Refresh's tooltip is read, not photographed: a tooltip opened on a window
// nobody sees is moved onto a monitor (WindowShortcutBugNotes.md). It goes
// back up last, so the walk goes on where it was.
internal static partial class AtelierSelfCheck
{
    private static readonly TimeSpan SizesPatience = TimeSpan.FromSeconds(30);

    private static async Task WalkAtelierSizesAsync(
        AtelierWindow window, string sample, Func<string, Func<Task>?, Task> capture)
    {
        SizesSample.AddTo(sample);
        // Coming back to the tool reuses the report the walk already took; the
        // second show is a Refresh, which measures the sample as it now is.
        await capture("sizes-finished", async () =>
        {
            await window.ShowToolForReviewAsync("sizes");
            await window.ShowToolForReviewAsync("sizes");
        });
        await capture("sizes-heavy", () => window.SelectForReviewAsync(Path.Combine("Vidéos", "Rushes", "C0001.MP4")));
        await capture("sizes-subfolder", () => window.OpenForReviewAsync("Vidéos"));
        await window.OpenForReviewAsync("..");

        string[] held = [Path.Combine(sample, "Projets"), Path.Combine(sample, "Musique")];
        SizesReview.Hold(window, folder => held.Contains(folder, FolderPaths.Comparer));
        Task? analysis = null;
        try
        {
            await capture("sizes-running", async () =>
            {
                analysis = window.RefreshForReviewAsync();
                await SizesReview.WaitUntilHeldAsync(window, SizesPatience);
            });
            await capture("sizes-stopped", async () =>
            {
                SizesReview.Stop(window);
                if (analysis is not null) await analysis.WaitAsync(SizesPatience);
            });
        }
        finally
        {
            SizesReview.Release(window);
            if (analysis is not null) await analysis.WaitAsync(SizesPatience);
        }

        await capture("sizes-refreshed", async () =>
        {
            await window.RefreshForReviewAsync();
            if (!SizesReview.ShowsCompleteReport(window))
                throw new InvalidOperationException("Refresh on a stopped analysis did not measure the folder again.");
            string sizesLabel = window.RefreshLabelForReview;
            if (sizesLabel.Split('\n').Length != 2)
                throw new InvalidOperationException($"Refresh's tooltip in the sizes tool does not say when the last analysis ran: \"{sizesLabel}\".");
            await window.ShowToolForReviewAsync("files");
            string filesLabel = window.RefreshLabelForReview;
            if (filesLabel == sizesLabel || filesLabel.Contains('\n'))
                throw new InvalidOperationException($"Refresh's tooltip kept the sizes tool's words in the file tool: \"{filesLabel}\".");
            await window.ShowToolForReviewAsync("sizes");
        });

        await capture("sizes-empty", () => window.OpenForReviewAsync(Path.Combine("Projets", SizesSample.EmptyFolder)));
        await window.OpenForReviewAsync(Path.Combine("..", ".."));

        IDisposable? unreadable = null;
        try
        {
            await capture("sizes-unreadable", () =>
            {
                unreadable = SizesSample.AddUnreadableFolder(sample, out string locked);
                return window.OpenForReviewAsync(Path.GetRelativePath(sample, locked));
            });
            await window.OpenForReviewAsync(Path.Combine("..", ".."));
        }
        finally
        {
            unreadable?.Dispose();
        }
    }
}
