using System.Collections.ObjectModel;
using System.Diagnostics;
using Deckle.Llm.Rewrite;
using Deckle.Localization;
using Deckle.Transcription;
using Microsoft.UI.Xaml;

namespace Deckle.App;

// Several files at once (specs/deckle-transcribe-and-rewrite.md, Several files).
// The list takes the field's place: each file is transcribed in turn and its
// .txt written beside its audio as soon as its text comes, so a long batch
// loses nothing if Deckle quits. A line shows the opening of its text; "Show
// more" opens the whole text in the field, where it can be read, corrected,
// rewritten and saved like any other, and "All files" goes back. Rewrite all
// rewrites every file of the list and writes each rewrite over its .txt.
// FileTranscriptionRun does the work; this part only shows it.
public sealed partial class TranscribeRewriteWindow
{
    private readonly ObservableCollection<FileTranscriptionItem> _files = [];
    private bool _showingFiles;

    private async Task TranscribeFilesAsync(IReadOnlyList<string> paths)
    {
        if (TranscribeFile is not { } transcribe) return;

        // A new choice replaces the list: the earlier files are already on disk.
        _files.Clear();
        foreach (string path in paths)
        {
            var item = new FileTranscriptionItem(path);
            _files.Add(item);
            _ = ReadDurationAsync(item);
        }
        HideNotice();
        ShowFiles();

        var run = new FileTranscriptionRun(
            path => transcribe(path, _ => { }), null, TranscriptFileWriter.Write, File.WriteAllText);
        BeginFilesRun();
        try
        {
            await run.RunAsync([.. _files]);
        }
        finally
        {
            EndFilesRun();
        }
    }

    private async Task RewriteFilesAsync(RewriteProfile profile)
    {
        if (Rewrite is not { } rewriteWith) return;
        BeginFilesRun();
        try
        {
            await FileTranscriptionRun.RewriteAllAsync(
                [.. _files], text => rewriteWith(text, profile).Text ?? "", File.WriteAllText);
        }
        finally
        {
            EndFilesRun();
        }
    }

    // A run of the list locks what would start another, as a run of the field
    // does; each line shows its own progress, so there is no outline.
    private void BeginFilesRun()
    {
        _running = true;
        UpdateCommands();
    }

    private void EndFilesRun()
    {
        _running = false;
        UpdateCommands();
    }

    private void ShowFiles()
    {
        _showingFiles = true;
        EditorArea.Visibility = Visibility.Collapsed;
        TextCommands.Visibility = Visibility.Collapsed;
        BackToFilesButton.Visibility = Visibility.Collapsed;
        FileList.Visibility = Visibility.Visible;
        UpdateCommands();
    }

    // fromList: the field shows a file of the list, so the way back stays.
    private void ShowField(bool fromList)
    {
        _showingFiles = false;
        FileList.Visibility = Visibility.Collapsed;
        EditorArea.Visibility = Visibility.Visible;
        TextCommands.Visibility = Visibility.Visible;
        BackToFilesButton.Visibility = fromList ? Visibility.Visible : Visibility.Collapsed;
        UpdateCommands();
    }

    // The whole text in the field, one step of Undo like any replacement. Save
    // then offers the audio's name in its folder, as for a file transcribed
    // into the field.
    private void OnShowMoreClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FileTranscriptionItem item) return;
        _history.Record(ReadText());
        WriteText(item.Text);
        _sourceAudioPath = item.Path;
        HideNotice();
        ShowField(fromList: true);
    }

    private void OnBackToFilesClick(object sender, RoutedEventArgs e)
    {
        HideNotice();
        ShowFiles();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FileTranscriptionItem { OutputPath: { } path }) return;
        try
        {
            // Explorer opens the folder with the .txt selected.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "open-folder", "exception", ex.GetType().Name, ex.Message);
        }
    }

    // The duration Windows' property system reads for the file; nothing shown
    // for a format it does not describe.
    private static async Task ReadDurationAsync(FileTranscriptionItem item)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.Path);
            var music = await file.Properties.GetMusicPropertiesAsync();
            TimeSpan d = music.Duration;
            if (d > TimeSpan.Zero)
                item.DurationText = d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss");
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "read-duration", "exception", ex.GetType().Name, ex.Message);
        }
    }

    // What a file's line says under its name.
    public static string StatusOf(FileTranscriptionState state, bool rewriteFailed, string? outputPath) => state switch
    {
        FileTranscriptionState.Waiting => Loc.Get("FileTranscription_StateWaiting"),
        FileTranscriptionState.Transcribing => Loc.Get("FileTranscription_StateTranscribing"),
        FileTranscriptionState.Rewriting => Loc.Get("FileTranscription_StateRewriting"),
        FileTranscriptionState.Done => Loc.Format(
            rewriteFailed ? "FileTranscription_StateSavedNotRewritten" : "FileTranscription_StateSaved",
            // The name alone: the .txt sits beside its audio, and Open folder shows where.
            Path.GetFileName(outputPath ?? "")),
        FileTranscriptionState.TranscriptionFailed => Loc.Get("FileTranscription_StateTranscriptionFailed"),
        FileTranscriptionState.WriteFailed => Loc.Get("FileTranscription_StateWriteFailed"),
        _ => "",
    };
}
