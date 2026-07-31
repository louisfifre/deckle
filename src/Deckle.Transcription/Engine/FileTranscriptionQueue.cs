using System.Threading.Channels;

namespace Deckle.Transcription;

// FIFO boundary between tray selections (producers) and the transcription
// engine (the single consumer). Starting is deliberately delegated back to the
// engine so this type owns ordering only; the engine keeps sole authority over
// its state machine and decides when the next item may leave the queue.
internal sealed class FileTranscriptionQueue
{
    private readonly Channel<string> _paths = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly object _gate = new();
    private int _count;

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    public int Enqueue(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        int added = 0;
        lock (_gate)
        {
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (!_paths.Writer.TryWrite(path))
                    break;

                _count++;
                added++;
            }
        }
        return added;
    }

    // Peeks first and removes only after the engine confirms Started. A busy
    // state therefore leaves the head untouched for the next Idle transition.
    public bool TryStartNext(Func<string, ToggleResult> start)
    {
        ArgumentNullException.ThrowIfNull(start);

        lock (_gate)
        {
            if (!_paths.Reader.TryPeek(out string? path))
                return false;

            if (start(path) != ToggleResult.Started)
                return false;

            if (!_paths.Reader.TryRead(out string? startedPath) || startedPath != path)
                throw new InvalidOperationException("File transcription queue order changed while starting its head.");

            _count--;
            return true;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _paths.Writer.TryComplete();
        }
    }
}
