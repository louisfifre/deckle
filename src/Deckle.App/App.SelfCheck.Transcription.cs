using Deckle.Hosting.SelfCheck;
using Deckle.Transcription;

namespace Deckle.App;

// ── Self-check: the Transcription page under Parakeet ────────────────────────
//
// A fresh data root chooses Whisper, and the Transcription page then hides
// what only Parakeet uses, the Lexicon fold among it. A review pass would
// never see it. So, after the walk, the run chooses Parakeet in its own root,
// writes a few sample lexicon terms, opens every fold and photographs the page
// again (settings-whisper-parakeet). Only this run's root is written, never
// the maintainer's, and no engine is composed: the page only reads settings.
public partial class App
{
    // Sample words of no person's: product names a reader recognizes.
    private static readonly string[] SampleLexiconTerms = ["Deckle", "WinUI", "Parakeet", "PowerToys"];

    private static void SeedParakeetSample()
    {
        TranscriptionSettings transcription = TranscriptionSettingsService.Instance.Current;
        transcription.Engine.Backend = AsrBackendKind.Parakeet;
        transcription.Lexicon.Terms = [.. SampleLexiconTerms];
        TranscriptionSettingsService.Instance.Save();
    }

    private static async Task CaptureTranscriptionParakeetAsync(
        SelfCheckReport report, Settings.SettingsWindow settings, string outputFolder, string captures)
    {
        SeedParakeetSample();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool loaded = await settings.ShowPageAsync(typeof(Transcription.Views.WhisperPage));
        long elapsed = clock.ElapsedMilliseconds;
        // The walk may have left this page current: reload it so it reads the seed.
        (settings.CurrentPage as Transcription.Views.WhisperPage)?.ViewModel.Load();

        await CaptureOpenPageAsync(report, settings, outputFolder, captures, loaded, elapsed,
            "whisper-parakeet", "Transcription (Parakeet, every fold open)");
    }
}
