using Deckle.Diagnostics;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Deckle.App;

// One file of the window's file list (specs/transcribe-and-rewrite/deckle-transcribe-and-rewrite.md,
// Several files): its path, its duration once read, where its run stands and
// the text it holds. The window binds to it; the run changes it.
public sealed class FileTranscriptionItem(string path) : INotifyPropertyChanged
{
    // The opening of the text a line shows, cut here so a three-hour
    // transcript never reaches a TextBlock whole: the line shows two lines
    // of it, and a click on the line opens the whole text in the field.
    private const int PreviewLength = 400;

    public string Path { get; } = path;

    public string Name { get; } = System.IO.Path.GetFileName(path);

    public string DurationText { get => _durationText; set => Set(ref _durationText, value); }
    private string _durationText = "";

    // The text written in the .txt: the transcript, then its rewrite.
    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(HasText));
        }
    }
    private string _text = "";

    public string Preview => _text.Length <= PreviewLength ? _text : _text[..PreviewLength];

    public bool HasText => _text.Length > 0;

    public FileTranscriptionState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(IsDone));
        }
    }
    private FileTranscriptionState _state;

    // The .txt written beside the audio, once Done.
    public string? OutputPath { get => _outputPath; set => Set(ref _outputPath, value); }
    private string? _outputPath;

    // Done, but with the raw transcript: the rewrite came back empty.
    public bool RewriteFailed { get => _rewriteFailed; set => Set(ref _rewriteFailed, value); }
    private bool _rewriteFailed;

    // The .txt holds a rewrite, no longer the raw transcript.
    public bool Rewritten { get => _rewritten; set => Set(ref _rewritten, value); }
    private bool _rewritten;

    public bool IsWorking => State is FileTranscriptionState.Transcribing or FileTranscriptionState.Rewriting;

    public bool IsDone => State == FileTranscriptionState.Done;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum FileTranscriptionState
{
    Waiting,
    Transcribing,
    Rewriting,
    Done,
    TranscriptionFailed,
    WriteFailed,
    // Stop came before its turn: the file was never transcribed.
    Stopped,
}

// The file list's run: each file in turn, transcribed, written beside its
// audio, then rewritten in place when a rewrite was asked. A file that fails,
// whatever throws, never stops the others. The raw transcript reaches the disk
// before the rewrite starts: a rewrite that comes back empty or throws, or a
// Deckle that quits during a rewrite of several minutes, still leaves it there,
// so hours of transcription are never lost to a provider.
//
// The steps are handed in, so the run is plain code a test drives: transcribe
// returns null for a failed run, as TranscriptionEngine.TranscribeFileToTextAsync
// does; rewrite returns the rewritten text or blank; write returns the path it
// wrote and throws on a failed write, as TranscriptFileWriter.Write does;
// overwrite replaces that file's text. Started on the UI thread, every change
// of an item lands back on it.
internal sealed class FileTranscriptionRun(
    Func<string, Task<string?>> transcribe,
    Func<string, string>? rewrite,
    Func<string, string, string> write,
    Action<string, string> overwrite)
{
    // Every waiting file, in order. The list is read live, by index, so files
    // added during the run join its end. Stop lets the file in progress
    // finish (the engine has no way to drop a file halfway) and marks the
    // ones still waiting as Stopped.
    public async Task RunAsync(IReadOnlyList<FileTranscriptionItem> items, CancellationToken stop = default)
    {
        for (int i = 0; i < items.Count; i++)
        {
            FileTranscriptionItem item = items[i];
            if (item.State != FileTranscriptionState.Waiting) continue;
            if (stop.IsCancellationRequested)
            {
                item.State = FileTranscriptionState.Stopped;
                continue;
            }
            try
            {
                await RunOneAsync(item);
            }
            catch (Exception ex)
            {
                DeckleWindowingSource.Log.WindowOperationFailed(
                    "transcribe-rewrite", "file", "exception", ex.GetType().Name, ex.Message);
                item.State = item.State == FileTranscriptionState.Transcribing
                    ? FileTranscriptionState.TranscriptionFailed
                    : FileTranscriptionState.WriteFailed;
            }
        }
    }

    private async Task RunOneAsync(FileTranscriptionItem item)
    {
        item.State = FileTranscriptionState.Transcribing;
        string? text = await transcribe(item.Path);
        if (string.IsNullOrWhiteSpace(text))
        {
            item.State = FileTranscriptionState.TranscriptionFailed;
            return;
        }

        string path;
        try
        {
            path = write(text, item.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleWindowingSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "write", "exception", ex.GetType().Name, ex.Message);
            item.State = FileTranscriptionState.WriteFailed;
            return;
        }
        item.OutputPath = path;
        item.Text = text;

        if (rewrite is { } rewriteText)
            await RewriteOneAsync(item, rewriteText, overwrite);

        item.State = FileTranscriptionState.Done;
    }

    // The list's Rewrite all: each file that holds a text, in turn, rewritten
    // from that text and written over its .txt. The others are left as they
    // are. A rewrite that fails keeps the file's text, on disk and on screen.
    public static async Task RewriteAllAsync(
        IReadOnlyList<FileTranscriptionItem> items,
        Func<string, string> rewrite,
        Action<string, string> overwrite,
        CancellationToken stop = default)
    {
        foreach (FileTranscriptionItem item in items)
        {
            if (stop.IsCancellationRequested) return;
            if (item.State != FileTranscriptionState.Done || !item.HasText || item.OutputPath is null) continue;
            try
            {
                await RewriteOneAsync(item, rewrite, overwrite);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DeckleWindowingSource.Log.WindowOperationFailed(
                    "transcribe-rewrite", "write", "exception", ex.GetType().Name, ex.Message);
                item.RewriteFailed = true;
            }
            item.State = FileTranscriptionState.Done;
        }
    }

    private static async Task RewriteOneAsync(
        FileTranscriptionItem item, Func<string, string> rewrite, Action<string, string> overwrite)
    {
        item.State = FileTranscriptionState.Rewriting;
        item.RewriteFailed = false;
        string text = item.Text;
        string rewritten;
        try
        {
            rewritten = await Task.Run(() => rewrite(text));
        }
        catch (Exception ex)
        {
            DeckleWindowingSource.Log.WindowOperationFailed(
                "transcribe-rewrite", "rewrite", "exception", ex.GetType().Name, ex.Message);
            rewritten = "";
        }

        if (string.IsNullOrWhiteSpace(rewritten))
        {
            item.RewriteFailed = true;
            return;
        }
        overwrite(item.OutputPath!, rewritten);
        item.Text = rewritten;
        item.Rewritten = true;
    }
}
