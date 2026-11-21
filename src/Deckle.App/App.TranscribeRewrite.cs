using Deckle.Hosting;
using System.Diagnostics;
using Deckle.Diagnostics;
using Deckle.Llm.Rewrite;

namespace Deckle.App;

// The Transcribe and rewrite window, opened from the tray. The App hands it
// the shared transcription engine (null while speech is not set up), one
// rewrite service built from the live provider list, and the way to the
// Rewriting page of Settings.
public partial class App
{
    private TranscribeRewriteWindow? _transcribeRewriteWindow;
    private RewriteService? _windowRewriteService;

    private void ShowTranscribeRewriteWindowLazy()
    {
        EnsureTranscribeRewriteWindow().ShowAndActivate();
        DeckleAppSource.Log.WindowOpened("transcribe-rewrite");
    }

    // The window created and wired, not shown: the self-check shows it without
    // activating it (SelfCheck/OffscreenWindow.cs).
    private TranscribeRewriteWindow EnsureTranscribeRewriteWindow()
    {
        if (_transcribeRewriteWindow is null)
        {
            var sw = Stopwatch.StartNew();
            _windowRewriteService ??= new RewriteService();
            RewriteService rewriter = _windowRewriteService;
            var window = WindowLifecycle.CreateOrLog("transcribe-rewrite", () => new TranscribeRewriteWindow
            {
                TranscribeFile = _engine is { } engine
                    ? (path, onUtterance) => engine.TranscribeFileToTextAsync(path, onUtterance)
                    : null,
                Rewrite = rewriter.Rewrite,
                OnShowRewriteSettingsRequested = () => ShowSettingsWindowLazy(
                    Llm.Rewrite.Views.LlmSettingsModule.PageTag),
            });
            WindowPlacement.Restore(window, SecondaryWindowPlacement.TranscribeRewrite);
            window.AppWindow.Closing += (_, _) =>
                WindowPlacement.Save(window, SecondaryWindowPlacement.TranscribeRewrite);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_transcribeRewriteWindow, window)) _transcribeRewriteWindow = null;
            };

            _transcribeRewriteWindow = window;
            WindowTheme.Apply(window);
            DeckleWindowingSource.Log.WindowLoadComplete("transcribe-rewrite", sw.ElapsedMilliseconds);
        }
        return _transcribeRewriteWindow;
    }
}
