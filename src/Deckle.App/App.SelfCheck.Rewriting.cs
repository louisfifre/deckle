using Deckle.App.SelfCheck;
using Deckle.Llm.Rewrite;
using Deckle.Vision;

namespace Deckle.App;

// ── Self-check: the Rewriting page in both modes of its dictation steps ──────
//
// A fresh data root has "Rewrite dictations" off, and the page then shows
// that one switch: the steps, the timeline and "Rewrite while I speak" would
// never be seen by a review pass. So the run writes the example settings of
// specs/deckle-dictation-rewriting.md into its own root before the walk, with
// "Rewrite while I speak" on, which the walk photographs; then it switches
// the mode off and photographs the page again (settings-llm-at-stop). Only
// this run's root is written, never the maintainer's.
public partial class App
{
    // The spec's example: a first step from 1,000 in blocks of 600, a second
    // from 4,000 in blocks of 2,000, a third from 12,000 in blocks of 6,000,
    // on the profiles a fresh root ships (the first, the second, the first).
    private static void SeedRewritingSample(bool whileSpeaking)
    {
        LlmSettings llm = LlmSettingsService.Instance.Current;
        if (llm.Profiles.Count < 2) return;
        string light = llm.Profiles[0].Id, heavy = llm.Profiles[1].Id;

        llm.Dictation.Enabled = true;
        llm.Dictation.StartAtLength = true;
        llm.Dictation.RewriteWhileSpeaking = whileSpeaking;
        llm.Dictation.Steps =
        [
            new RewriteStep { ProfileId = light, FromChars = 1000, BlockChars = 600 },
            new RewriteStep { ProfileId = heavy, FromChars = 4000, BlockChars = 2000 },
            new RewriteStep { ProfileId = light, FromChars = 12000, BlockChars = 6000 },
        ];
        LlmSettingsService.Instance.Save();
    }

    // The page again with the mode off, after the walk left it.
    private static async Task CaptureRewritingAtStopAsync(
        SelfCheckReport report, Settings.SettingsWindow settings, string outputFolder, string captures)
    {
        SeedRewritingSample(whileSpeaking: false);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool loaded = await settings.ShowPageAsync(typeof(Llm.Rewrite.Views.LlmPage));
        const string slug = "llm-at-stop";
        SelfCheckSurface surface = new("settings", slug, "Rewriting (at Stop)", loaded, clock.ElapsedMilliseconds,
            null, 0, 0, "page-render");
        if (loaded && settings.CurrentPage is { } page)
        {
            try
            {
                ElementImage.Result image = await ElementImage.CapturePageAsync(
                    page, Path.Combine(captures, $"settings-{slug}"));
                string[] files = [.. image.Files.Select(f => Path.GetRelativePath(outputFolder, f))];
                surface = surface with
                {
                    Png = files[0],
                    Parts = files.Length > 1 ? files : null,
                    Width = image.Width,
                    Height = image.Height,
                };
            }
            catch (Exception ex)
            {
                surface = surface with { Error = $"{ex.GetType().Name}: {ex.Message}" };
            }
        }
        report.Surfaces.Add(surface);
    }
}
