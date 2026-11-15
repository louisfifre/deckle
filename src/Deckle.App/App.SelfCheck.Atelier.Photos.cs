using Deckle.App.SelfCheck;
using Deckle.Atelier;
using Deckle.Atelier.Views;
using Deckle.Vision;

namespace Deckle.App;

// The self-check's walk of the photo tool, on a shoot made from nothing
// (SamplePhotos) inside the sample folder: the gallery, a photo selected with
// its panel, marks set with their counters, a photo seen large, and the
// summary of "Appliquer les marques…", then the file tool on the same shoot,
// as a gallery and with a photo selected. The summary is photographed open and
// then closed with "Annuler": the self-check never sends a file to the
// Recycle Bin. Marks a kept data root may hold are cleared first, so the
// gallery is photographed without any.
public partial class App
{
    private static readonly string[] SampleKept =
        ["DSC_0201.JPG", "DSC_0202.JPG", "DSC_0204.JPG", "DSC_0206.JPG", "DSC_0207.JPG", "DSC_0209.JPG", "DSC_0210.JPG"];

    private static readonly string[] SampleDeleted = ["DSC_0203.JPG", "DSC_0208.JPG", "DSC_0216.JPG"];

    private async Task WalkAtelierPhotosAsync(
        SelfCheckReport report, InterfaceCaptureRun run, string outputFolder, Action<string> step,
        AtelierWindow window, string sample, SelfCheckWalkGate gate)
    {
        gate.ThrowIfDone();
        SamplePhotos.Write(Path.Combine(sample, "Photos", "2026"));

        async Task Capture(string state, Func<Task> reach, bool keepPopups = false)
        {
            gate.ThrowIfDone();
            bool photographed = gate.Photographs(state);
            step($"atelier/{state}");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await reach();
                await Task.Delay(400);
            }
            catch (Exception ex)
            {
                report.Surfaces.Add(new SelfCheckSurface("atelier", state, null, false, clock.ElapsedMilliseconds, null, 0, 0,
                    "window-capture", Error: $"{ex.GetType().Name}: {ex.Message}"));
                report.Save(outputFolder);
                return;
            }
            if (!photographed) return;
            (int closed, int left) = keepPopups ? (0, 0) : await CloseTransientPopupsAsync(window);
            report.Surfaces.Add(await CaptureWindowFrameAsync(
                run, window, "atelier", state, null, true, clock.ElapsedMilliseconds, closed, left));
            report.Save(outputFolder);
        }

        await Capture("photos-gallery", async () =>
        {
            await window.OpenForReviewAsync("2026");
            await window.OpenForReviewAsync(SamplePhotos.ShootFolder);
            await window.ShowToolForReviewAsync("photos");
            await PhotosReview.MarkAsync(window, SamplePhotos.Names, PhotoMark.None);
        });

        await Capture("photos-selected", () => window.SelectForReviewAsync("DSC_0207.JPG"));

        await Capture("photos-marked", async () =>
        {
            await PhotosReview.MarkAsync(window, SampleKept, PhotoMark.Keep);
            await PhotosReview.MarkAsync(window, SampleDeleted, PhotoMark.Delete);
        });

        await Capture("photos-large", () => PhotosReview.ShowLargeAsync(window, "DSC_0207.JPG"));
        PhotosReview.LeaveLarge(window);

        await Capture("photos-apply-summary", async () =>
        {
            if (!await PhotosReview.OpenApplySummaryAsync(window))
                throw new InvalidOperationException("The summary of the marks did not open.");
        }, keepPopups: true);
        PhotosReview.CloseApplySummary(window);
        await Task.Delay(300);

        // The file tool's gallery on the same shoot: the one folder of the
        // sample whose files Windows draws a picture of, so the tiles show
        // their content and not only their type's icon. Back to the list
        // after, which the later states photograph.
        await Capture("files-gallery-photos", async () =>
        {
            await window.ShowToolForReviewAsync("files");
            await FilesReview.ShowDisplayAsync(window, FileListDisplay.Gallery);
            await Task.Delay(600);
        });

        // A photo selected in the file list: the photo tool's panel, with the
        // mark the walk set above, and no counters (12.15).
        await Capture("files-photo-selected", async () =>
        {
            await FilesReview.ShowDisplayAsync(window, FileListDisplay.List);
            await window.SelectForReviewAsync("DSC_0207.JPG");
        });
        await FilesReview.ShowDisplayAsync(window, FileListDisplay.List);
    }
}
