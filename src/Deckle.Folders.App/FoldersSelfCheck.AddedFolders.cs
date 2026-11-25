using static Deckle.Hosting.SelfCheck.SelfCheckCapture;
using Deckle.Hosting.SelfCheck;
using Deckle.Folders.Views;
using Deckle.Vision;

namespace Deckle.Folders.App;

// The self-check's walk of the added folders' removal and of the states no
// board draws (spec, Open 33), called last from WalkFoldersAsync, once the
// folder icons' walk has added the sample's folders. It photographs the pane
// opened with a folder's remove button shown; a folder that vanished, in the
// rail and in the opened pane, with its message in the centre; and a folder
// whose name is too long for the address bar, at the window's minimum width.
//
// Everything it adds, deletes or removes lives under the walk's own
// self-check-sample folder: the folder that vanishes is one it made there a
// moment before. Removing a folder only forgets its entry; nothing presses a
// button that opens an application.
internal static partial class FoldersSelfCheck
{
    // The size the window is drawn at, in DIPs.
    private const int FoldersDrawnWidth = 1280;
    private const int FoldersDrawnHeight = 800;

    private static async Task WalkFoldersAddedFoldersAsync(
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

        static void Check(bool holds, string what)
        {
            if (!holds) throw new InvalidOperationException(what);
        }

        string documents = Path.Combine(sample, "Documents");
        await Capture("folders-more-button", async () =>
        {
            await window.AddFolderForReviewAsync(sample);
            window.SetPaneOpenForReview(true);
            await Task.Delay(300);
            Check(window.ShowMoreButtonForReview(documents), "The \"...\" button did not show on the item.");
        });
        window.HideMoreButtonsForReview();

        // The folder's menu, the same under "..." and at a right click, in the
        // opened pane and in the pane reduced to icons.
        foreach (bool paneOpen in (bool[])[true, false])
        {
            await Capture(paneOpen ? "folders-menu" : "folders-menu-reduced", async () =>
            {
                window.SetPaneOpenForReview(paneOpen);
                await Task.Delay(300);
                IReadOnlyList<string> labels = window.ShowFolderMenuForReview(documents);
                Check(labels.Count == 4, $"The folder's menu holds {labels.Count} items, not 4.");
                await Task.Delay(300);
            }, keepPopups: true);
            window.HideFolderMenuForReview();
        }
        window.SetPaneOpenForReview(true);

        // The folder the window stands in goes; the window moves to the one
        // that takes its place in the pane, and Back no longer leads into it.
        await Capture("folders-removed-current", async () =>
        {
            await window.AddFolderForReviewAsync(documents);
            await window.RemoveFolderForReviewAsync(documents);
            Check(window.CurrentFolderForReview is { } now && !string.Equals(now, documents, StringComparison.OrdinalIgnoreCase),
                "The window still stands in the removed folder.");
            // The removed item is out of the pane and the garbage collector
            // runs right behind: the pane still closes and opens
            // (FolderPaneBugNotes.md).
            await Task.Delay(60);
            CollectGarbageNow();
            window.SetPaneOpenForReview(false);
            window.SetPaneOpenForReview(true);
        });
        window.SetPaneOpenForReview(false);

        // A folder made for the walk, added, then deleted from the disk: the
        // window is refreshed as F5 does.
        string samples = Path.GetDirectoryName(sample)!;
        string vanished = Path.Combine(samples, "Dossier disparu");
        await Capture("folder-missing", async () =>
        {
            Directory.CreateDirectory(vanished);
            await window.AddFolderForReviewAsync(vanished);
            Directory.Delete(vanished, recursive: true);
            await window.RefreshForReviewAsync();
            Check(window.ShowsUnreachableForReview(vanished), "The centre does not say the folder is missing.");
            Check(await window.IsMarkedUnreachableForReviewAsync(vanished), "The pane does not mark the missing folder.");
        });
        await Capture("folder-missing-pane", () =>
        {
            window.SetPaneOpenForReview(true);
            return Task.CompletedTask;
        });
        window.SetPaneOpenForReview(false);
        await window.RemoveFolderForReviewAsync(vanished);

        // A name longer than the field, in the file tool, in the field's full
        // form.
        string longName = Path.Combine(samples,
            "Un dossier au nom vraiment très long, bien plus long que la barre d'adresse ne peut le montrer");
        await Capture("address-long-name", async () =>
        {
            Directory.CreateDirectory(longName);
            await window.AddFolderForReviewAsync(longName);
            ResizeFolders(window, LongNameWidth, FoldersDrawnHeight);
            await Task.Delay(400);
            // The segments show in the full form, where a long one is cut.
            Check(window.OpenAddressFullForReview(), "The address field did not open its full form.");
            await Task.Delay(400);
            Check(window.AddressCutsASegmentForReview, "The long segment of the address bar is not cut.");
        });
        ResizeFolders(window, FoldersDrawnWidth, FoldersDrawnHeight);
    }

    // Narrow enough that the long name does not fit, wide enough for the
    // drive and the icon to leave it room: the window's narrow layouts are
    // photographed by their own states.
    private const int LongNameWidth = 1000;

    private static void ResizeFolders(FoldersWindow window, int widthDip, int heightDip)
    {
        double scale = window.Content.XamlRoot?.RasterizationScale ?? 1.0;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(widthDip * scale), (int)(heightDip * scale)));
    }
}
