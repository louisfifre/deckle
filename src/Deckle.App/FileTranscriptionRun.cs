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

// The assistant's run: each file in turn, transcribed, rewritten when a
// rewrite was asked, and written beside its audio. A file that fails never
// stops the others. A rewrite that comes back empty still saves the raw
// transcript, so hours of transcription are never lost to a provider error.
//
// The steps are handed in, so the run is plain code a test drives: transcribe
// returns null for a failed run, as TranscriptionEngine.TranscribeFileToTextAsync
// does; rewrite returns the rewritten text or blank; write returns the path it
// wrote and throws on a failed write, as TranscriptFileWriter.Write does.
// Started on the UI thread, every change of an item lands back on it.
internal sealed class FileTranscriptionRun(
    Func<string, Task<string?>> transcribe,
    Func<string, string>? rewrite,
    Func<string, string, string> write)
{
    public async Task RunAsync(IReadOnlyList<FileTranscriptionItem> items)
    {
        foreach (FileTranscriptionItem item in items)
        {
            item.State = FileTranscriptionState.Transcribing;
            string? text = await transcribe(item.Path);
            if (string.IsNullOrWhiteSpace(text))
            {
                item.State = FileTranscriptionState.TranscriptionFailed;
                continue;
            }

            if (rewrite is { } rewriteText)
            {
                item.State = FileTranscriptionState.Rewriting;
                string transcript = text;
                string rewritten = await Task.Run(() => rewriteText(transcript));
                if (string.IsNullOrWhiteSpace(rewritten)) item.RewriteFailed = true;
                else text = rewritten;
            }

            try
            {
                item.OutputPath = write(text, item.Path);
                item.State = FileTranscriptionState.Done;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DeckleAppSource.Log.WindowOperationFailed(
                    "file-transcription", "write", "exception", ex.GetType().Name, ex.Message);
                item.State = FileTranscriptionState.WriteFailed;
            }
        }
    }
}
