using static Deckle.Hosting.SelfCheck.SelfCheckCapture;
using Deckle.Hosting.SelfCheck;
using Deckle.Folders.Views;
using Deckle.Core;
using Deckle.Vision;

namespace Deckle.Folders.App;

// The self-check's walk of the added folders' icons, called last from
// WalkFoldersAsync so the states before it keep their one added folder. It
// photographs the icon picker open under the address bar's icon (12.3), then
// adds four folders of the sample beside it, gives each an icon and a colour
// the way a tile of the picker does, and photographs the pane reduced to its
// icons and opened, then the picker of a folder whose icon sits on its
// square. Nothing outside the sample folder is added or changed.
internal static partial class FoldersSelfCheck
{
    private static readonly (string[] Path, IconChoice Icon)[] SampleFolderIcons =
    [
        ([], new IconChoice(IconSymbol.Folder, IconTint.Accent, false)),
        (["Photos"], new IconChoice(IconSymbol.Photos, IconTint.Orange, true)),
        (["Documents"], new IconChoice(IconSymbol.Documents, IconTint.Green, false)),
        (["Documents", "Notes"], new IconChoice(IconSymbol.Favorite, IconTint.Red, false)),
        (["Projets"], new IconChoice(IconSymbol.Code, IconTint.Purple, true)),
    ];

    private static async Task WalkFoldersFolderIconsAsync(
        SelfCheckReport report, InterfaceCaptureRun run, string outputFolder, Action<string> step,
        FoldersWindow window, string sample, SelfCheckWalkGate gate)
    {
        gate.ThrowIfDone();

        async Task Capture(string state, Func<Task> reach, bool keepPopups = false)
        {
            gate.ThrowIfDone();
            bool photographed = gate.Photographs(state);
            step($"folders/{state}");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await reach();
                await Task.Delay(400);
            }
            catch (Exception ex)
            {
                report.Surfaces.Add(new SelfCheckSurface("folders", state, null, false, clock.ElapsedMilliseconds, null, 0, 0,
                    "window-capture", Error: $"{ex.GetType().Name}: {ex.Message}"));
                report.Save(outputFolder);
                return;
            }
            if (!photographed) return;
            (int closed, int left) = keepPopups ? (0, 0) : await CloseTransientPopupsAsync(window);
            report.Surfaces.Add(await CaptureWindowFrameAsync(
                run, window, "folders", state, null, true, clock.ElapsedMilliseconds, closed, left));
            report.Save(outputFolder);
        }

        async Task OpenPicker()
        {
            if (!window.OpenIconPickerForReview())
                throw new InvalidOperationException("The icon picker did not open.");
            await Task.Delay(300);
        }

        await Capture("folder-icon-picker", async () =>
        {
            await window.AddFolderForReviewAsync(sample);
            window.ChangeFolderIconForReview(sample, SampleFolderIcons[0].Icon);
            await OpenPicker();
        }, keepPopups: true);
        window.CloseIconPickerForReview();
        await Task.Delay(300);

        await Capture("folder-icons-pane", async () =>
        {
            foreach ((string[] parts, IconChoice icon) in SampleFolderIcons)
            {
                string path = Path.Combine([sample, .. parts]);
                await window.AddFolderForReviewAsync(path);
                window.ChangeFolderIconForReview(path, icon);
            }
            await window.AddFolderForReviewAsync(sample);
        });

        // The pane's items just changed and the garbage collector runs right
        // behind: the pane once refused to open then (FolderPaneBugNotes.md).
        await Capture("folder-icons-pane-open", async () =>
        {
            await window.AddFolderForReviewAsync(sample);
            await Task.Delay(60);
            CollectGarbageNow();
            window.SetPaneOpenForReview(true);
        });

        // Home with the pane open and the five folders added; then a tile
        // opens a folder, and Back returns to Home with Forward enabled.
        await Capture("folders-home", () =>
        {
            window.NavigateHomeForReview();
            if (!window.AtHomeForReview) throw new InvalidOperationException("The window does not stand at Home.");
            return Task.CompletedTask;
        });
        await Capture("folders-home-back", async () =>
        {
            if (!window.OpenHomeTileForReview(Path.Combine(sample, "Photos")))
                throw new InvalidOperationException("The Home page has no tile for the Photos folder.");
            await Task.Delay(300);
            if (window.AtHomeForReview) throw new InvalidOperationException("A tile did not open its folder.");
            window.GoBackForReview();
            if (!window.AtHomeForReview) throw new InvalidOperationException("Back did not return to Home.");
            if (!window.CanGoForwardForReview) throw new InvalidOperationException("Forward is not enabled after Back to Home.");
        });
        window.SetPaneOpenForReview(false);

        await Capture("folder-icon-picker-background", async () =>
        {
            await window.AddFolderForReviewAsync(Path.Combine(sample, "Photos"));
            await OpenPicker();
        }, keepPopups: true);
        window.CloseIconPickerForReview();
        await Task.Delay(300);

        // Each added folder remembers its tool: the sample goes to Photos, the
        // Photos folder added inside it still opens in Files, and coming back
        // to the sample finds Photos. Both are put back to Files afterwards so
        // the later scenes keep their look.
        await Capture("tool-per-folder", async () =>
        {
            string other = Path.Combine(sample, "Photos");
            await window.AddFolderForReviewAsync(sample);
            await window.ShowToolForReviewAsync("photos");
            await window.AddFolderForReviewAsync(other);
            if (window.ActiveToolIdForReview != "files")
                throw new InvalidOperationException(
                    $"The second added folder opened in {window.ActiveToolIdForReview}, not in Files: the first one's tool carried over.");
            await window.AddFolderForReviewAsync(sample);
            if (window.ActiveToolIdForReview != "photos")
                throw new InvalidOperationException(
                    $"The first added folder opened in {window.ActiveToolIdForReview} on coming back, not in Photos: it did not keep its tool.");
        });
        await window.ShowToolForReviewAsync("files");
        await window.AddFolderForReviewAsync(Path.Combine(sample, "Photos"));
        await window.ShowToolForReviewAsync("files");
    }

    // What the garbage collector does on its own time, done at a moment the
    // walk chooses, so a state that depends on it fails every run or none.
    private static void CollectGarbageNow()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
