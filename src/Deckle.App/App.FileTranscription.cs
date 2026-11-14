using System.Diagnostics;
using Deckle.Diagnostics;
using Deckle.Llm.Rewrite;

namespace Deckle.App;

// The file assistant, opened from the tray's "Transcribe audio to text…": audio
// files in, a .txt beside each out (FileTranscriptionWindow). While speech is
// not set up, the entry says so instead of opening a window that could not run.
public partial class App
{
    private FileTranscriptionWindow? _fileTranscriptionWindow;

    private void ShowFileTranscriptionWindowLazy()
    {
        // Called from a tray click: nothing here may take the tray down.
        try
        {
            if (_engine is null)
            {
                EmitSpeechNotSetUp();
                return;
            }
            EnsureFileTranscriptionWindow().ShowAndActivate();
            DeckleAppSource.Log.WindowOpened("file-transcription");
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.FileTranscriptionEntryFailed(ex.GetType().Name, ex.Message);
        }
    }

    // The window created and wired, not shown: the self-check shows it without
    // activating it (SelfCheck/OffscreenWindow.cs).
    private FileTranscriptionWindow EnsureFileTranscriptionWindow()
    {
        if (_fileTranscriptionWindow is null)
        {
            var sw = Stopwatch.StartNew();
            _windowRewriteService ??= new RewriteService();
            RewriteService rewriter = _windowRewriteService;
            var window = CreateWindowOrLog("file-transcription", () => new FileTranscriptionWindow
            {
                TranscribeFile = _engine is { } engine
                    ? path => engine.TranscribeFileToTextAsync(path)
                    : null,
                Rewrite = rewriter.Rewrite,
                OnShowRewriteSettingsRequested = () => ShowSettingsWindowLazy(
                    Llm.Rewrite.Views.LlmSettingsModule.PageTag),
            });
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.FileTranscription);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.FileTranscription);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_fileTranscriptionWindow, window)) _fileTranscriptionWindow = null;
            };

            _fileTranscriptionWindow = window;
            ApplyThemeToSingle(window);
            DeckleWindowingSource.Log.WindowLoadComplete("file-transcription", sw.ElapsedMilliseconds);
        }
        return _fileTranscriptionWindow;
    }
}
