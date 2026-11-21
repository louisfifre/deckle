using Deckle.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App;

// A file of the list opened in the field is already saved: the list says so.
// So every change made to it there, a typed correction or a rewrite, is
// written back to its .txt, a moment after the typing stops and again when the
// field leaves it (back to the list, another file, the window closed). There is
// no Save to remember, as in Windows Settings; a change that cannot be written
// says so, and Copy keeps the text reachable.
public sealed partial class TranscribeRewriteWindow
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(800);

    // The file of the list the field shows; null for a text of its own.
    private FileTranscriptionItem? _openFile;
    private DispatcherQueueTimer? _autoSaveTimer;

    // The field's text as it stood when it was last in step with the .txt: at
    // the opening, then after each write. RichEdit hands text back with its
    // own line breaks, so comparing with the file's text would rewrite a file
    // that was only looked at.
    private string _savedText = "";

    private void ScheduleAutoSave()
    {
        if (_openFile is null) return;
        if (_autoSaveTimer is null)
        {
            _autoSaveTimer = DispatcherQueue.CreateTimer();
            _autoSaveTimer.IsRepeating = false;
            _autoSaveTimer.Interval = AutoSaveDelay;
            _autoSaveTimer.Tick += (_, _) => SaveOpenFile();
        }
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    // Writes the field over the open file's .txt when it differs from it.
    private void SaveOpenFile()
    {
        _autoSaveTimer?.Stop();
        if (_openFile is not { OutputPath: { } path } file) return;
        string text = ReadText();
        if (text == _savedText) return;
        try
        {
            File.WriteAllText(path, text);
            _savedText = text;
            file.Text = text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleWindowingSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "autosave", "exception", ex.GetType().Name, ex.Message);
            ShowNotice(InfoBarSeverity.Error,
                "TranscribeRewrite_AutoSaveFailed_Title", "TranscribeRewrite_AutoSaveFailed_Body");
        }
    }

    private void CloseOpenFile()
    {
        SaveOpenFile();
        _openFile = null;
    }
}
