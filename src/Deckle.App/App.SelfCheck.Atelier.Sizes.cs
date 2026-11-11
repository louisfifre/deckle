using Deckle.Atelier;
using Deckle.Atelier.Views;

namespace Deckle.App;

// The self-check's walk of the sizes tool, called from WalkAtelierAsync once
// every tool has been photographed. It adds to the sample folder what the
// sizes need (SizesSample: sparse files, so nothing weighs on the disk), then
// photographs the analysis finished, one of the heaviest files selected (12.21),
// and one folder down, which re-roots the report instead of reading the disk
// again. It goes back up last, so the walk goes on where it was.
public partial class App
{
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
    }
}
