using System.Threading.Channels;

namespace Deckle.Transcription;

// FIFO boundary between tray selections and the transcription engine. One
// immutable selection leaves the queue as one engine lifecycle, so files from a
// later picker action can never interleave the active batch.
internal sealed class FileTranscriptionQueue
{
    private readonly Channel<FileTranscriptionBatch> _batches =
        Channel.CreateUnbounded<FileTranscriptionBatch>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly object _gate = new();
    private int _fileCount;

    public int Count
    {
        get { lock (_gate) return _fileCount; }
    }

    public int Enqueue(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string[] accepted = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        if (accepted.Length == 0)
            return 0;

        lock (_gate)
        {
            if (!_batches.Writer.TryWrite(new FileTranscriptionBatch(accepted)))
                return 0;

            _fileCount += accepted.Length;
        }
        return accepted.Length;
    }

    // Peeks first and removes only after the engine confirms Started. A busy
    // state therefore leaves the head untouched for the next Idle transition.
    public bool TryStartNext(Func<FileTranscriptionBatch, ToggleResult> start)
    {
        ArgumentNullException.ThrowIfNull(start);

        lock (_gate)
        {
            if (!_batches.Reader.TryPeek(out FileTranscriptionBatch? batch))
                return false;

            if (start(batch) != ToggleResult.Started)
                return false;

            if (!_batches.Reader.TryRead(out FileTranscriptionBatch? startedBatch)
                || !ReferenceEquals(startedBatch, batch))
            {
                throw new InvalidOperationException(
                    "File transcription queue order changed while starting its head.");
            }

            _fileCount -= batch.AudioFilePaths.Count;
            return true;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _batches.Writer.TryComplete();
        }
    }
}

internal sealed record FileTranscriptionBatch(IReadOnlyList<string> AudioFilePaths);
