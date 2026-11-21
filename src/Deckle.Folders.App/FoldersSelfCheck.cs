using static Deckle.Hosting.SelfCheck.SelfCheckCapture;
using Deckle.Contributions;
using Deckle.Hosting.SelfCheck;
using Deckle.Folders;
using Deckle.Folders.Views;
using Deckle.Core;
using Deckle.Vision;

namespace Deckle.Folders.App;

// The self-check's walk of Deckle Folders window. The run's data root is fresh,
// so the window first opens with no folder: the empty state of board 12.11.
// Then a sample folder made under that root (SampleFolder, nothing from the
// person's disk) is added the way the picker adds one, and the window is
// photographed in the file tool, with an item selected, with a row's two
// actions shown, in its large rows and its gallery (each with a row's actions
// too), in each registered tool, with the pane open, and one folder down.
// A state that fails to show
// fails its image, and so the run.
internal static partial class FoldersSelfCheck
{
    // Run by the harness through FoldersContribution (FoldersWalkContext). With a view for target,
    // the walk is played up to it and ends there (SelfCheckWalkGate): that end
    // is not a failure.
    internal static async Task RunFoldersWalkAsync(FoldersWalkContext context)
    {
        var gate = new SelfCheckWalkGate(context.TargetView);
        try { await WalkFoldersAsync(context.Host, context.Report, context.Run, context.OutputFolder, context.Step, gate); }
        catch (OperationCanceledException) when (gate.Done) { }
        if (context.TargetView is not null && !gate.Reached) context.Report.Views = gate.Offered;
    }

    private static async Task WalkFoldersAsync(
        ContributionHost host, SelfCheckReport report, InterfaceCaptureRun run, string outputFolder, Action<string> step,
        SelfCheckWalkGate gate)
    {
        UseReviewPhotoDescriptions();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var window = (FoldersWindow)host.Ensure("folders");
        OffscreenWindow.ShowInactive(window);
        bool loaded = await WaitUntilLoadedAsync(window);

        // Every state is reached; the gate says which ones get a picture, and
        // ends the walk once a targeted state has its own.
        async Task Capture(string state, Func<Task>? reach = null)
        {
            gate.ThrowIfDone();
            bool photographed = gate.Photographs(state);
            step($"folders/{state}");
            var stateClock = System.Diagnostics.Stopwatch.StartNew();
            if (!loaded)
            {
                report.Surfaces.Add(new SelfCheckSurface("folders", state, null, false, clock.ElapsedMilliseconds, null, 0, 0, "window-capture"));
                return;
            }
            try
            {
                if (reach is not null) await reach();
                await Task.Delay(400);
            }
            catch (Exception ex)
            {
                report.Surfaces.Add(new SelfCheckSurface("folders", state, null, false, stateClock.ElapsedMilliseconds, null, 0, 0,
                    "window-capture", Error: $"{ex.GetType().Name}: {ex.Message}"));
                report.Save(outputFolder);
                return;
            }
            if (!photographed) return;
            (int closed, int left) = await CloseTransientPopupsAsync(window);
            report.Surfaces.Add(await CaptureWindowFrameAsync(
                run, window, "folders", state, null, true, stateClock.ElapsedMilliseconds, closed, left));
            report.Save(outputFolder);
        }

        // A run kept with -KeepData may already hold the sample: the empty
        // state is then not there to photograph.
        if (!window.HasAddedFolders) await Capture("empty");

        string sample = SampleFolder.Create(Path.Combine(AppPaths.UserDataRoot, "self-check-sample"));
        await Capture("files", () => window.AddFolderForReviewAsync(sample));
        await Capture("files-selected", () => window.SelectForReviewAsync("notes-2023.zip"));

        // The other two displays, with the item still selected and its panel
        // open, as changing display leaves them; then back to the list, which
        // the later states photograph.
        // A row with its two actions, in each display, as the pointer over it
        // shows them: another row than the selected one, so both are seen.
        // Nothing presses them: the walk never opens File Explorer nor sends
        // a file to the Recycle Bin.
        const string hovered = "Tarifs-2026.pdf";
        await Capture("files-row-actions", () => FilesReview.ShowRowActionsAsync(window, hovered));
        if (loaded) FilesReview.HideRowActions(window);

        await Capture("files-large-rows", () => FilesReview.ShowDisplayAsync(window, FileListDisplay.LargeRows));
        await Capture("files-large-rows-actions", () => FilesReview.ShowRowActionsAsync(window, hovered));
        if (loaded) FilesReview.HideRowActions(window);
        await Capture("files-gallery", () => FilesReview.ShowDisplayAsync(window, FileListDisplay.Gallery));
        await Capture("files-gallery-actions", () => FilesReview.ShowRowActionsAsync(window, hovered));
        if (loaded) FilesReview.HideRowActions(window);
        if (loaded) await WalkFoldersPreviewsAsync(window, Capture);
        if (loaded) await FilesReview.ShowDisplayAsync(window, FileListDisplay.List);
        if (loaded) await WalkFoldersFileSelectionAsync(window, Capture);

        foreach (string tool in window.ToolIds.Where(t => t != "files"))
            await Capture(tool, () => window.ShowToolForReviewAsync(tool));
        await WalkFoldersSizesAsync(window, sample, Capture);

        await Capture("pane-open", async () =>
        {
            await window.ShowToolForReviewAsync("files");
            window.SetPaneOpenForReview(true);
        });
        window.SetPaneOpenForReview(false);

        await Capture("files-subfolder", () => window.OpenForReviewAsync("Photos"));
        if (loaded) await WalkFoldersPhotosAsync(report, run, outputFolder, step, window, sample, gate);

        await WalkFoldersMapAsync(window, sample, Capture);

        // The chevron's list is a popup, which every image closes first, so
        // only the text mode is photographed.
        await Capture("address-text", () =>
        {
            window.EditAddressForReview();
            return Task.CompletedTask;
        });

        if (loaded) await WalkFoldersFolderIconsAsync(report, run, outputFolder, step, window, sample, gate);
        if (loaded) await WalkFoldersSearchAsync(report, run, outputFolder, step, window, gate);
        if (loaded) await WalkFoldersAddedFoldersAsync(report, run, outputFolder, step, window, sample, gate);
    }
}
