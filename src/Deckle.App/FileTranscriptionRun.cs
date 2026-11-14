using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Deckle.App;

// One file of the file assistant (specs/deckle-transcribe-and-rewrite.md, File
// assistant): its path, its duration once read, and where its run stands. The
// window binds to it; the run changes it.
public sealed class FileTranscriptionItem(string path) : INotifyPropertyChanged
{
    public string Path { get; } = path;

    public string Name { get; } = System.IO.Path.GetFileName(path);

    public string DurationText { get => _durationText; set => Set(ref _durationText, value); }
    private string _durationText = "";

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
}

// The assistant's run: each file in turn, transcribed, written beside its
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
    public async Task RunAsync(IReadOnlyList<FileTranscriptionItem> items)
    {
        foreach (FileTranscriptionItem item in items)
        {
            try
            {
                await RunOneAsync(item);
            }
            catch (Exception ex)
            {
                DeckleAppSource.Log.WindowOperationFailed(
                    "file-transcription", "file", "exception", ex.GetType().Name, ex.Message);
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
            DeckleAppSource.Log.WindowOperationFailed(
                "file-transcription", "write", "exception", ex.GetType().Name, ex.Message);
            item.State = FileTranscriptionState.WriteFailed;
            return;
        }
        item.OutputPath = path;

        if (rewrite is { } rewriteText)
        {
            item.State = FileTranscriptionState.Rewriting;
            string rewritten;
            try
            {
                rewritten = await Task.Run(() => rewriteText(text));
            }
            catch (Exception ex)
            {
                DeckleAppSource.Log.WindowOperationFailed(
                    "file-transcription", "rewrite", "exception", ex.GetType().Name, ex.Message);
                rewritten = "";
            }

            if (string.IsNullOrWhiteSpace(rewritten)) item.RewriteFailed = true;
            else overwrite(path, rewritten);
        }

        item.State = FileTranscriptionState.Done;
    }
}
