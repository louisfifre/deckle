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
            PickFileResult? picked = await picker.PickSingleFileAsync();
            if (picked is null) return;

            string before = ReadText();
            _history.Record(before);
            BeginRun(ProcessingVariant.Transcribing);
            WriteText("");

            // Each utterance reaches the end of the field as it comes, joined
            // as the engine joins them: a space, or a blank line after a pause.
            StartUtteranceFeed();
            string? text = await TranscribeFile(picked.Path, utterance =>
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
                _sourceAudioPath = picked.Path;
                HideNotice();
            }
        }
        catch (Exception ex)
        {
            if (_running) EndRun();
            DeckleAppSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "transcribe", "exception", ex.GetType().Name, ex.Message);
        }
        finally
        {
            UpdateCommands();
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

            string before = ReadText();
            s_lastProfileId = profile.Id;
            BeginRun(ProcessingVariant.Rewriting);
            Func<string, RewriteProfile, GenerationResult> rewrite = Rewrite;
            GenerationResult result = await Task.Run(() => rewrite(before, profile));
            EndRun();

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                ShowNotice(InfoBarSeverity.Error,
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
            DeckleAppSource.Log.WindowOperationFailed(
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
            DeckleAppSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "copy", "exception", ex.GetType().Name, ex.Message);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveAccelerator(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (SaveButton.IsEnabled) await SaveAsync();
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
            DeckleAppSource.Log.WindowOperationFailed(
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

    // Copy, Save and Rewrite need a text; nothing that changes the field runs
    // during a run.
    private void UpdateCommands()
    {
        bool hasText = ReadText().Trim().Length > 0;
        TranscribeFileButton.IsEnabled = !_running;
        RewriteButton.IsEnabled = !_running && hasText;
        ProfileBox.IsEnabled = !_running;
        UndoButton.IsEnabled = !_running && _history.CanUndo;
        RedoButton.IsEnabled = !_running && _history.CanRedo;
        CopyButton.IsEnabled = hasText;
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
