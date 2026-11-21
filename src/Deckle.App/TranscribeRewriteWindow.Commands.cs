using Deckle.Diagnostics;
using Deckle.Composition;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Localization;
using Deckle.Transcription;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;

namespace Deckle.App;

// What the buttons do. A run (a transcription or a rewrite) locks the field
// and the commands that would change it, wears the HUD outline, and on
// failure leaves the field as it was before it started.
public sealed partial class TranscribeRewriteWindow
{
    // The formats the tray's file transcription accepts.
    private static readonly string[] AudioExtensions =
        [".mp3", ".m4a", ".aac", ".wav", ".flac", ".wma", ".ogg", ".opus"];

    private bool _running;

    // The audio the field's text came from, for Save's suggested name and
    // folder; null for a pasted text.
    private string? _sourceAudioPath;

    // One file goes into the field; several at once go to the file list, and
    // once a list exists every file chosen joins it.
    private async void OnTranscribeFileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (TranscribeFile is null)
            {
                ShowNotice(InfoBarSeverity.Warning,
                    "TranscribeRewrite_SpeechNotSetUp_Title", "TranscribeRewrite_SpeechNotSetUp_Body");
                return;
            }

            var picker = new FileOpenPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
            };
            foreach (string extension in AudioExtensions)
                picker.FileTypeFilter.Add(extension);
            IReadOnlyList<PickFileResult> picked = await picker.PickMultipleFilesAsync();
            if (picked.Count == 0) return;

            if (picked.Count > 1 || _files.Count > 0)
                await AddFilesAsync([.. picked.Select(p => p.Path)]);
            else
                await TranscribeIntoFieldAsync(picked[0].Path);
        }
        catch (Exception ex)
        {
            if (_running) EndRun();
            DeckleWindowingSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "transcribe", "exception", ex.GetType().Name, ex.Message);
        }
        finally
        {
            UpdateCommands();
        }
    }

    private async Task TranscribeIntoFieldAsync(string audioPath)
    {
        if (TranscribeFile is null) return;

        ShowField();
        string before = ReadText();
        _history.Record(before);
        BeginRun(ProcessingVariant.Transcribing);
        WriteText("");

        // Each utterance reaches the end of the field as it comes, joined
        // as the engine joins them: a space, or a blank line after a pause.
        StartUtteranceFeed();
        string? text = await TranscribeFile(audioPath, utterance =>
            DispatcherQueue.TryEnqueue(() => QueueUtterance(utterance)));
        StopUtteranceFeed();

        EndRun();
        if (string.IsNullOrWhiteSpace(text))
        {
            _history.Withdraw();
            WriteText(before);
            ShowNotice(InfoBarSeverity.Error,
                "TranscribeRewrite_TranscribeFailed_Title", "TranscribeRewrite_TranscribeFailed_Body");
        }
        else
        {
            // The whole text once more: the field holds exactly what the
            // engine delivered, whatever the utterances showed.
            WriteText(text);
            _sourceAudioPath = audioPath;
            HideNotice();
        }
    }

    private async void OnRewriteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Rewrite is null || ProfileBox.SelectedItem is not RewriteProfile profile) return;

            // A profile with no provider would only come back empty: say what
            // is missing and where to set it, before sending anything.
            if (string.IsNullOrWhiteSpace(profile.ProviderId) || string.IsNullOrWhiteSpace(profile.Model))
            {
                ShowNotice(InfoBarSeverity.Warning,
                    "TranscribeRewrite_NoProvider_Title", "TranscribeRewrite_NoProvider_Body",
                    withSettings: true);
                return;
            }

            s_lastProfileId = profile.Id;
            if (_showingFiles)
            {
                await RewriteFilesAsync(profile);
                return;
            }

            string before = ReadText();
            BeginRun(ProcessingVariant.Rewriting);
            Func<string, RewriteProfile, GenerationResult> rewrite = Rewrite;
            GenerationResult result = await Task.Run(() => rewrite(before, profile));
            EndRun();

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                ShowNotice(InfoBarSeverity.Warning,
                    "TranscribeRewrite_RewriteFailed_Title", "TranscribeRewrite_RewriteFailed_Body",
                    withSettings: true);
                return;
            }

            _history.Record(before);
            WriteText(result.Text);
            HideNotice();
        }
        catch (Exception ex)
        {
            if (_running) EndRun();
            DeckleWindowingSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "rewrite", "exception", ex.GetType().Name, ex.Message);
        }
        finally
        {
            UpdateCommands();
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (_history.Undo(ReadText()) is { } text) WriteText(text);
        UpdateCommands();
    }

    private void OnRedoClick(object sender, RoutedEventArgs e)
    {
        if (_history.Redo(ReadText()) is { } text) WriteText(text);
        UpdateCommands();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(ReadText());
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            DeckleWindowingSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "copy", "exception", ex.GetType().Name, ex.Message);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_openFile is not null) SaveOpenFile();
        else if (SaveButton.IsEnabled) await SaveAsync();
    }

    // "Save as", on the audio's name in its folder when the text came from a
    // file, as the tray's transcription names its .txt.
    private async Task SaveAsync()
    {
        string text = ReadText();
        if (text.Length == 0) return;

        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = _sourceAudioPath is null
                    ? Loc.Get("TranscribeRewrite_DefaultFileName")
                    : Path.GetFileNameWithoutExtension(_sourceAudioPath),
            };
            if (_sourceAudioPath is not null && Path.GetDirectoryName(_sourceAudioPath) is { } folder)
                picker.SuggestedFolder = folder;
            picker.FileTypeChoices.Add(Loc.Get("TranscribeRewrite_TextFileType"), [".txt"]);

            PickFileResult? file = await picker.PickSaveFileAsync();
            if (file is not null)
                await File.WriteAllTextAsync(file.Path, text);
        }
        catch (Exception ex)
        {
            DeckleWindowingSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "save", "exception", ex.GetType().Name, ex.Message);
        }
    }

    private void OnRewriteSettingsClick(object sender, RoutedEventArgs e)
        => OnShowRewriteSettingsRequested?.Invoke();

    private void BeginRun(ProcessingVariant variant)
    {
        _running = true;
        Editor.IsReadOnly = true;
        ShowOutline(variant);
        UpdateCommands();
    }

    private void EndRun()
    {
        _running = false;
        _feedTimer?.Stop();
        Editor.IsReadOnly = false;
        HideOutline();
        UpdateCommands();
    }

    // Copy, Save and Rewrite need a text, Rewrite all a saved file; nothing
    // that changes the field runs during its run, and nothing that changes
    // the files during theirs. On the list, the text commands give way to
    // Stop while a batch runs, and the rewrite row waits for a saved file.
    private void UpdateCommands()
    {
        bool hasText = !_showingFiles && ReadText().Trim().Length > 0;
        bool filesHaveText = _files.Any(f => f.HasText);

        // Files chosen during a transcription batch join it; not during a
        // Rewrite all, which would leave them untouched.
        TranscribeFileButton.IsEnabled = !_running && !_filesRewriting;

        // The button says where the files go before anything is chosen; once a
        // list exists every choice joins it, and the list says the rest.
        bool listExists = _files.Count > 0;
        TranscribeFileLabel.Text = Loc.Get(listExists ? "TranscribeRewrite_AddFiles" : "TranscribeRewrite_TranscribeFiles");
        ToolTipService.SetToolTip(TranscribeFileButton,
            listExists ? null : Loc.Get("TranscribeRewrite_TranscribeFilesTip"));

        TextCommands.Visibility = _showingFiles ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = _showingFiles && _filesRunning ? Visibility.Visible : Visibility.Collapsed;

        bool rewriteShown = !_showingFiles || filesHaveText;
        RewriteButton.Visibility = rewriteShown ? Visibility.Visible : Visibility.Collapsed;
        ProfileBox.Visibility = rewriteShown ? Visibility.Visible : Visibility.Collapsed;
        RewriteLabel.Text = Loc.Get(_showingFiles ? "TranscribeRewrite_RewriteAll" : "TranscribeRewrite_Rewrite");
        ToolTipService.SetToolTip(RewriteButton,
            _showingFiles ? Loc.Get("TranscribeRewrite_RewriteAllTip") : null);
        RewriteButton.IsEnabled = _showingFiles
            ? !_filesRunning && filesHaveText
            : !_running && !_filesRewriting && hasText;
        ProfileBox.IsEnabled = !_running && !_filesRunning;

        UndoButton.IsEnabled = !_running && _history.CanUndo;
        RedoButton.IsEnabled = !_running && _history.CanRedo;
        CopyButton.IsEnabled = hasText;
        // A file of the list saves itself.
        SaveButton.Visibility = _openFile is null ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = hasText;
    }

    private void ShowNotice(InfoBarSeverity severity, string titleKey, string bodyKey, bool withSettings = false)
    {
        Notice.Severity = severity;
        Notice.Title = Loc.Get(titleKey);
        Notice.Message = Loc.Get(bodyKey);
        Notice.ActionButton = withSettings ? NewSettingsAction() : null;
        Notice.IsOpen = true;
    }

    private void HideNotice() => Notice.IsOpen = false;

    private Button NewSettingsAction()
    {
        var button = new Button { Content = Loc.Get("TranscribeRewrite_OpenRewriteSettings") };
        button.Click += (_, _) => OnShowRewriteSettingsRequested?.Invoke();
        return button;
    }
}
