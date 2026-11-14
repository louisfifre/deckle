namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── FileToText partial — a file transcribed for a window, not for a .txt ──
    //
    // The Transcribe and rewrite window shows the transcript in its own field.
    // The file takes the same road as a tray selection: the FIFO queue, so a
    // dictation in progress delays it instead of refusing it, the prime, the
    // decode producer and the segmented consumer. Only the delivery changes:
    // the raw text goes back to the caller, nothing is written beside the
    // audio, nothing reaches the clipboard, and the engine never rewrites it
    // (the window rewrites on demand, with the profile it shows).
    //
    // onUtterance sees each utterance of this file as it is transcribed, so the
    // window can fill its field progressively. It is attached only while this
    // file crosses the consumer: a dictation that runs before or after never
    // reaches it. It is raised on the consumer thread; the caller marshals.
    //
    // A batch still queued when the engine is disposed never starts, so its
    // task never completes: that happens only while the app closes, together
    // with the window awaiting it.

    /// <summary>
    /// Transcribes one audio file and returns its raw text, or null when the
    /// run delivered nothing (engine disposed, decode failure, no speech,
    /// failed or cancelled transcription; the person was told by the usual
    /// message where one applies). Never throws for a failed run.
    /// </summary>
    public Task<string?> TranscribeFileToTextAsync(
        string audioFilePath, Action<TranscribedUtterance>? onUtterance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        if (_disposed) return Task.FromResult<string?>(null);

        var returnText = new FileTextReturn(onUtterance);
        if (_fileTranscriptionQueue.Enqueue([audioFilePath], returnText) == 0)
            return Task.FromResult<string?>(null);

        TryConsumeNextFileTranscription();
        return returnText.Text;
    }
}

// The caller's end of a file batch whose transcript is returned. Settle runs
// when the worker ends, whatever the outcome, so the caller's await always
// completes; it is a no-op after Deliver.
internal sealed class FileTextReturn(Action<TranscribedUtterance>? onUtterance)
{
    private readonly TaskCompletionSource<string?> _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Action<TranscribedUtterance>? OnUtterance { get; } = onUtterance;

    public Task<string?> Text => _done.Task;

    public void Deliver(string text) => _done.TrySetResult(text);

    public void Settle() => _done.TrySetResult(null);
}
