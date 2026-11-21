using static Deckle.Hosting.SelfCheck.SelfCheckCapture;
using Deckle.App.SelfCheck;
using Deckle.Hosting.SelfCheck;
using Deckle.Atelier;
using Deckle.Atelier.Views;
using Deckle.Core;
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

        await WalkAtelierPhotoDescriptionsAsync(window, (state, reach) => Capture(state, reach));

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

    // ── Descriptions ──────────────────────────────────────────────────────
    //
    // The panel's description in its states and the folder run, with the
    // model the walk gives the app (ReviewPhotoDescriptionModel): it answers
    // what each state needs and never reaches a model server. The photos are
    // the walk's own sample; the descriptions it writes land in the run's
    // data root, cleared before the window opens (UseReviewPhotoDescriptions).

    private static readonly ReviewPhotoDescriptionModel ReviewDescriber = new();

    // Called before the Atelier window is built, since the window's tools
    // take the model when they are made. A kept data root may hold the last
    // run's descriptions: they go, so "no description yet" is there to see.
    private static void UseReviewPhotoDescriptions()
    {
        AtelierToolCatalog.PhotoDescriptionModel = ReviewDescriber;
        AtelierToolCatalog.PhotoDescriptionReadiness = null;
        string stored = Path.Combine(AppPaths.GetModuleDirectory(Deckle.Modules.ModuleIds.Atelier), PhotoDescriptionStore.DirectoryName);
        try { if (Directory.Exists(stored)) Directory.Delete(stored, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task WalkAtelierPhotoDescriptionsAsync(
        AtelierWindow window, Func<string, Func<Task>, Task> capture)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(20);

        await capture("photos-description-none", () => window.SelectForReviewAsync("DSC_0205.JPG"));

        await capture("photos-description-progress", async () =>
        {
            ReviewDescriber.Hold();
            await PhotosReview.DescribeSelectedAsync(window);
        });

        await capture("photos-description-written", async () =>
        {
            ReviewDescriber.Answer(_ => ReviewPhotoDescriptionModel.Written());
            await PhotosReview.WaitForSelectedAsync(window, timeout);
        });

        await capture("photos-description-failed", async () =>
        {
            ReviewDescriber.Answer(_ => PhotoDescriptionResult.Failed(PhotoDescriptionFailure.ServerUnreachable));
            await window.SelectForReviewAsync("DSC_0211.JPG");
            await PhotosReview.DescribeSelectedAsync(window);
            await PhotosReview.WaitForSelectedAsync(window, timeout);
        });

        await capture("photos-description-stopped", async () =>
        {
            ReviewDescriber.Hold();
            await window.SelectForReviewAsync("DSC_0212.JPG");
            await PhotosReview.DescribeSelectedAsync(window);
            PhotosReview.StopSelected(window);
            await PhotosReview.WaitForSelectedAsync(window, timeout);
        });

        // The folder run under way: five photos answered, then the model
        // holds, so the counter, the bar and the way to stop stay on screen.
        await capture("photos-describe-folder", async () =>
        {
            await window.SelectForReviewAsync("DSC_0205.JPG");
            ReviewDescriber.Answer(n => n < 5 ? ReviewPhotoDescriptionModel.Written() : null);
            await PhotosReview.DescribeFolderAsync(window);
            await PhotosReview.WaitForFolderAsync(window, r => r is { Described: >= 6 }, timeout);
        });

        // Stopped, then run again to its end with one photo failing: the
        // line above the gallery says how many have a description and how
        // many failed.
        PhotosReview.StopFolder(window);
        await PhotosReview.WaitForFolderAsync(window, r => r is null, timeout);
        await capture("photos-describe-folder-ended", async () =>
        {
            ReviewDescriber.Answer(n => n == 0
                ? PhotoDescriptionResult.Failed(PhotoDescriptionFailure.Other)
                : ReviewPhotoDescriptionModel.Written());
            await PhotosReview.DescribeFolderAsync(window);
            await PhotosReview.WaitForFolderAsync(window, r => r is null, timeout);
        });
    }

    // The walk's model: it answers by the script the walk sets, numbering
    // the requests from the last change of script; a script that answers
    // null holds the request until the script changes or the request is
    // stopped. It reads nothing and sends nothing.
    private sealed class ReviewPhotoDescriptionModel : IPhotoDescriptionModel
    {
        private readonly object _lock = new();
        private Func<int, PhotoDescriptionResult?> _script = _ => null;
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _asked;

        // An invented sentence: no model wrote it.
        public static PhotoDescriptionResult Written() => PhotoDescriptionResult.Of(new PhotoDescription(
            "Deux personnes sous un arbre, en fin d'après-midi ; la lumière vient de la gauche.",
            "self-check", DateTimeOffset.Now));

        public void Hold() => Answer(_ => null);

        public void Answer(Func<int, PhotoDescriptionResult?> script)
        {
            TaskCompletionSource changed;
            lock (_lock)
            {
                _script = script;
                _asked = 0;
                changed = _changed;
                _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            changed.TrySetResult();
        }

        public async Task<PhotoDescriptionResult> DescribeAsync(byte[] jpeg, string prompt, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task changed;
                lock (_lock)
                {
                    if (_script(_asked) is { } answer)
                    {
                        _asked++;
                        return answer;
                    }
                    changed = _changed.Task;
                }
                try
                {
                    await changed.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return PhotoDescriptionResult.Failed(PhotoDescriptionFailure.Stopped);
                }
            }
        }
    }
}