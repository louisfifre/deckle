using Deckle.Hosting.SelfCheck;
using Deckle.Transcription;

namespace Deckle.App;

// ── Self-check: the Transcription pages under Parakeet ───────────────────────
//
// A fresh data root chooses Whisper, and the Transcription pages then hide
// what only Parakeet uses: the switch of the Lexicon page, the Parakeet model
// files of the General page. A review pass would never see them. So, after the
// walk, the run chooses Parakeet in its own root, writes a few sample lexicon
// terms, and photographs both pages again with every fold open
// (settings-whisper-parakeet, settings-lexicon-parakeet). Only this run's root
// is written, never the maintainer's, and no engine is composed: the pages
// only read settings.
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

        clock.Restart();
        loaded = await settings.ShowPageAsync(typeof(Transcription.Views.LexiconPage));
        elapsed = clock.ElapsedMilliseconds;
        await CaptureOpenPageAsync(report, settings, outputFolder, captures, loaded, elapsed,
            "lexicon-parakeet", "Lexicon (Parakeet)");
    }
}
