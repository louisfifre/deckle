using Deckle.App.SelfCheck;
using Deckle.Atelier;
using Deckle.Atelier.Views;
using Deckle.Core;
using Deckle.Vision;

namespace Deckle.App;

// The self-check's walk of the Atelier window. The run's data root is fresh,
// so the window first opens with no folder: the empty state of board 12.11.
// Then a sample folder made under that root (SampleFolder, nothing from the
// person's disk) is added the way the picker adds one, and the window is
// photographed in the file tool, with an item selected, in each registered
// tool, with the pane open, and one folder down. A state that fails to show
// fails its image, and so the run.
public partial class App
{
    private async Task WalkAtelierAsync(
        SelfCheckReport report, InterfaceCaptureRun run, string outputFolder, Action<string> step)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        AtelierWindow window = EnsureAtelierWindow();
        OffscreenWindow.ShowInactive(window);
        bool loaded = await WaitUntilLoadedAsync(window);

        async Task Capture(string state, Func<Task>? reach = null)
        {
            step($"atelier/{state}");
            var stateClock = System.Diagnostics.Stopwatch.StartNew();
            if (!loaded)
            {
                report.Surfaces.Add(new SelfCheckSurface("atelier", state, null, false, clock.ElapsedMilliseconds, null, 0, 0, "window-capture"));
                return;
            }
            try
            {
                if (reach is not null) await reach();
                await Task.Delay(400);
            }
            catch (Exception ex)
            {
                report.Surfaces.Add(new SelfCheckSurface("atelier", state, null, false, stateClock.ElapsedMilliseconds, null, 0, 0,
                    "window-capture", Error: $"{ex.GetType().Name}: {ex.Message}"));
                report.Save(outputFolder);
                return;
            }
            (int closed, int left) = await CloseTransientPopupsAsync(window);
            report.Surfaces.Add(await CaptureWindowFrameAsync(
                run, window, "atelier", state, null, true, stateClock.ElapsedMilliseconds, closed, left));
            report.Save(outputFolder);
        }

        // A run kept with -KeepData may already hold the sample: the empty
        // state is then not there to photograph.
        if (!window.HasAddedFolders) await Capture("empty");

        string sample = SampleFolder.Create(Path.Combine(AppPaths.UserDataRoot, "self-check-sample"));
        await Capture("files", () => window.AddFolderForReviewAsync(sample));
        await Capture("files-selected", () => window.SelectForReviewAsync("notes-2023.zip"));

        foreach (string tool in window.ToolIds.Where(t => t != "files"))
            await Capture(tool, () => window.ShowToolForReviewAsync(tool));
        await WalkAtelierSizesAsync(window, sample, Capture);

        await Capture("pane-open", async () =>
        {
            await window.ShowToolForReviewAsync("files");
            window.SetPaneOpenForReview(true);
        });
        window.SetPaneOpenForReview(false);

        await Capture("files-subfolder", () => window.OpenForReviewAsync("Photos"));
        if (loaded) await WalkAtelierPhotosAsync(report, run, outputFolder, step, window, sample);

        await WalkAtelierMapAsync(window, sample, Capture);

        // The chevron's list is a popup, which every image closes first, so
        // only the text mode is photographed.
        await Capture("address-text", () =>
        {
            window.EditAddressForReview();
            return Task.CompletedTask;
        });
    }
}
