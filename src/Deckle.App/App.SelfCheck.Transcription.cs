using Deckle.Hosting.SelfCheck;
using Deckle.Transcription;

namespace Deckle.App;

// ── Self-check: the Transcription pages with a lexicon ───────────────────────
//
// A fresh data root has an empty lexicon, so the walk photographs the Lexicon
// page without a single word. After the walk, the run writes a few sample
// lexicon terms in its own root and photographs the General and Lexicon pages
// again with every fold open (settings-whisper-parakeet,
// settings-lexicon-parakeet). Only this run's root is written, never the
// maintainer's, and no engine is composed: the pages only read settings.
public partial class App
{
    // Sample words of no person's: product names a reader recognizes.
    private static readonly string[] SampleLexiconTerms = ["Deckle", "WinUI", "Parakeet", "PowerToys"];

    private static void SeedLexiconSample()
    {
        TranscriptionSettings transcription = TranscriptionSettingsService.Instance.Current;
        transcription.Lexicon.Terms = [.. SampleLexiconTerms];
        TranscriptionSettingsService.Instance.Save();
    }

    private static async Task CaptureTranscriptionParakeetAsync(
        SelfCheckReport report, Settings.SettingsWindow settings, string outputFolder, string captures)
    {
        SeedLexiconSample();

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
