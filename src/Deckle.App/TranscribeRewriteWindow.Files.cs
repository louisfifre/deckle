using Deckle.Diagnostics;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Deckle.Llm.Rewrite;
using Deckle.Localization;
using Deckle.Transcription;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App;

// Several files at once (specs/deckle-transcribe-and-rewrite.md, Several files).
// The list takes the field's place: each file is transcribed in turn and its
// .txt written beside its audio as soon as its text comes, so a long batch
// loses nothing if Deckle quits. A header says how far the batch is and where
// the texts go, once for the whole list. A line holds the file's state, its
// name and the opening of its text; a click on it opens the whole text in the
// field, where every change is written back to the file's .txt (.AutoSave.cs).
// Files chosen while the list is there join it. FileTranscriptionRun does the
// work; this part only shows it.
public sealed partial class TranscribeRewriteWindow
{
    private readonly ObservableCollection<FileTranscriptionItem> _files = [];
    private bool _showingFiles;

    // A batch of the list runs (a transcription or a Rewrite all), apart from
    // _running, which belongs to the field: a file can be opened and edited
    // while the others are still transcribing.
    private bool _filesRunning;
    private bool _filesRewriting;
    private CancellationTokenSource? _stopFiles;

    private void InitializeFiles()
    {
        FileList.ItemsSource = _files;
        _files.CollectionChanged += OnFilesChanged;
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (FileTranscriptionItem item in e.NewItems)
                item.PropertyChanged += OnFileChanged;
        UpdateFilesSummary();
    }

    private void OnFileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileTranscriptionItem.State) or nameof(FileTranscriptionItem.Text))
        {
            UpdateFilesSummary();
            UpdateCommands();
        }
    }

    // Several files chosen with no list: a new list. Files chosen while a list
    // exists join it; a file already there that failed or was stopped is tried
    // again, one already done or waiting is left as it is.
    private async Task AddFilesAsync(IReadOnlyList<string> paths)
    {
        if (TranscribeFile is null) return;

        foreach (string path in paths)
        {
            FileTranscriptionItem? known = _files.FirstOrDefault(
                f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                var item = new FileTranscriptionItem(path);
                _files.Add(item);
                _ = ReadDurationAsync(item);
            }
            else if (known.State is FileTranscriptionState.Stopped
                     or FileTranscriptionState.TranscriptionFailed or FileTranscriptionState.WriteFailed)
            {
                known.State = FileTranscriptionState.Waiting;
            }
        }
        HideNotice();
        ShowFiles();

        // A run in progress reads the list live and takes the new files.
        if (_filesRunning) return;
        await RunFilesAsync();
    }

    private async Task RunFilesAsync()
    {
        if (TranscribeFile is not { } transcribe) return;
        var run = new FileTranscriptionRun(
            path => transcribe(path, _ => { }), null, TranscriptFileWriter.Write, File.WriteAllText);
        CancellationToken stop = BeginFilesRun(rewriting: false);
        try
        {
            // Again while files wait: one added just as a pass ended. After
            // Stop, one more pass marks whatever still waits as stopped.
            do await run.RunAsync(_files, stop);
            while (_files.Any(f => f.State == FileTranscriptionState.Waiting));
        }
        finally
        {
            EndFilesRun();
        }
    }

    private async Task RewriteFilesAsync(RewriteProfile profile)
    {
        if (Rewrite is not { } rewriteWith) return;
        CancellationToken stop = BeginFilesRun(rewriting: true);
        try
        {
            await FileTranscriptionRun.RewriteAllAsync(
                [.. _files], text => rewriteWith(text, profile).Text ?? "", File.WriteAllText, stop);
        }
        finally
        {
            EndFilesRun();
        }
    }

    // Each line shows its own progress, so the list has no outline.
    private CancellationToken BeginFilesRun(bool rewriting)
    {
        _filesRunning = true;
        _filesRewriting = rewriting;
        _stopFiles = new CancellationTokenSource();
        StopButton.IsEnabled = true;
        UpdateCommands();
        return _stopFiles.Token;
    }

    private void EndFilesRun()
    {
        ResetFilesRun();
        UpdateCommands();
    }

    // The batch is over, or the self-check moves to another state.
    private void ResetFilesRun()
    {
        _filesRunning = false;
        _filesRewriting = false;
        _stopFiles?.Dispose();
        _stopFiles = null;
    }

    // The file in progress finishes; the button stays, dimmed, until it has.
    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        _stopFiles?.Cancel();
        StopButton.IsEnabled = false;
    }

    private void ShowFiles()
    {
        CloseOpenFile();
        _showingFiles = true;
        EditorArea.Visibility = Visibility.Collapsed;
        FileHeader.Visibility = Visibility.Collapsed;
        FileList.Visibility = Visibility.Visible;
        FilesHeader.Visibility = Visibility.Visible;
        UpdateCommands();
    }

    private void ShowField()
    {
        CloseOpenFile();
        _showingFiles = false;
        FileList.Visibility = Visibility.Collapsed;
        FilesHeader.Visibility = Visibility.Collapsed;
        FileHeader.Visibility = Visibility.Collapsed;
        EditorArea.Visibility = Visibility.Visible;
        UpdateCommands();
    }

    private void OnFileItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FileTranscriptionItem { HasText: true } item) OpenFile(item);
    }

    // The whole text in the field, its file named above it. A fresh Undo: an
    // earlier text must never come back here, since it would be saved over
    // this file.
    private void OpenFile(FileTranscriptionItem item)
    {
        ShowField();
        _history.Clear();
        WriteText(item.Text);
        _openFile = item;
        _autoSave = new OpenFileAutoSave(item, ReadText());
        _sourceAudioPath = item.Path;
        FileCrumbs.ItemsSource = new[] { Loc.Get("TranscribeRewrite_AllFiles"), item.Name };
        FileHeader.Visibility = Visibility.Visible;
        HideNotice();
        UpdateCommands();
    }

    private void OnFileCrumbClick(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Index == 0)
        {
            HideNotice();
            ShowFiles();
        }
    }

    // The folder the texts go to: the first file's, its .txt selected once written.
    private void OnOpenFilesFolderClick(Microsoft.UI.Xaml.Documents.Hyperlink sender,
        Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
    {
        if (_files.FirstOrDefault() is { } first) ShowInFolder(first.OutputPath ?? first.Path);
    }

    private void OnShowInFolderClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FileTranscriptionItem item)
            ShowInFolder(item.OutputPath ?? item.Path);
    }

    private static void ShowInFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DeckleWindowingSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "open-folder", "exception", ex.GetType().Name, ex.Message);
        }
    }

    // "3 of 5 saved · 1 failed · 1 not transcribed".
    private void UpdateFilesSummary()
    {
        int saved = _files.Count(f => f.IsDone);
        int failed = _files.Count(f => f.State is FileTranscriptionState.TranscriptionFailed
                                                  or FileTranscriptionState.WriteFailed);
        int stopped = _files.Count(f => f.State == FileTranscriptionState.Stopped);
        var parts = new List<string> { Loc.Format("TranscribeRewrite_FilesSaved", saved, _files.Count) };
        if (failed > 0) parts.Add(Loc.Format("TranscribeRewrite_FilesFailed", failed));
        if (stopped > 0) parts.Add(Loc.Format("TranscribeRewrite_FilesStopped", stopped));
        FilesSummary.Text = string.Join(" · ", parts);
    }

    // The duration Windows' property system reads for the file, in words a
    // clock reading cannot be mistaken for; nothing for a format it does not
    // describe.
    private static async Task ReadDurationAsync(FileTranscriptionItem item)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.Path);
            var music = await file.Properties.GetMusicPropertiesAsync();
            if (music.Duration > TimeSpan.Zero) item.DurationText = DurationOf(music.Duration);
        }
        catch (Exception ex)
        {
            DeckleWindowingSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "read-duration", "exception", ex.GetType().Name, ex.Message);
        }
    }

    internal static string DurationOf(TimeSpan d) =>
        d.TotalHours >= 1 ? Loc.Format("FileTranscription_DurationHours", (int)d.TotalHours, d.Minutes)
        : d.TotalMinutes >= 1 ? Loc.Format("FileTranscription_DurationMinutes", (int)Math.Round(d.TotalMinutes))
        : Loc.Format("FileTranscription_DurationSeconds", (int)Math.Round(d.TotalSeconds));

    // The line under a file's name: its state when it is not simply saved,
    // then its duration.
    public static string DetailOf(FileTranscriptionState state, bool rewriteFailed, bool rewritten, string duration)
    {
        string status = state switch
        {
            FileTranscriptionState.Waiting => Loc.Get("FileTranscription_StateWaiting"),
            FileTranscriptionState.Transcribing => Loc.Get("FileTranscription_StateTranscribing"),
            FileTranscriptionState.Rewriting => Loc.Get("FileTranscription_StateRewriting"),
            FileTranscriptionState.Done when rewriteFailed => Loc.Get("FileTranscription_StateRewriteKept"),
            FileTranscriptionState.Done when rewritten => Loc.Get("FileTranscription_StateRewritten"),
            FileTranscriptionState.TranscriptionFailed => Loc.Get("FileTranscription_StateTranscriptionFailed"),
            FileTranscriptionState.WriteFailed => Loc.Get("FileTranscription_StateWriteFailed"),
            FileTranscriptionState.Stopped => Loc.Get("FileTranscription_StateStopped"),
            _ => "",
        };
        return string.Join(" · ", new[] { status, duration }.Where(s => s.Length > 0));
    }

    // One status slot per line: a ring while it works, then one mark.
    public static Visibility SavedMark(FileTranscriptionState state, bool rewriteFailed)
        => state == FileTranscriptionState.Done && !rewriteFailed ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CautionMark(FileTranscriptionState state, bool rewriteFailed)
        => state == FileTranscriptionState.Done && rewriteFailed ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility FailedMark(FileTranscriptionState state)
        => state is FileTranscriptionState.TranscriptionFailed or FileTranscriptionState.WriteFailed
            ? Visibility.Visible : Visibility.Collapsed;
}
