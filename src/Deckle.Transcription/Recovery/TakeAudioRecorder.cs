using System.Threading.Channels;

namespace Deckle.Transcription;

// ── TakeAudioRecorder ──────────────────────────────────────────────────────
//
// Sits between the capture thread and TakeAudioRecoveryStore. The capture
// thread must never wait on the disk or on DPAPI: a wait that overruns the
// driver's ring drops audio. So Push only copies the frame into an in-memory
// block, and a full block (about a second) is handed to a writer task that
// seals and appends it.
//
// Lifetime: one recorder per dictation take. Complete flushes the partial
// block, waits for the writer to drain, and closes the store's handle; only
// then does the engine decide whether the file stays or goes, so the file it
// keeps is whole up to the last frame.
//
// Threads: Push runs on the capture thread, Complete on the worker thread
// (the same one, once Record has returned, but nothing here relies on it), and
// the writer is its own task. One lock guards the block and the completed flag;
// it is held for a copy of at most one frame, never across I/O.
internal sealed class TakeAudioRecorder
{
    // About a second at 16 kHz: one DPAPI call and one write per second.
    internal const int BlockSamples = 16_000;

    // The writer drains in milliseconds; the bound only keeps a stuck disk from
    // holding the end of the take. A writer still busy after it is left to
    // finish on its own, and the store refuses what comes after the close.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private readonly TakeAudioRecoveryStore _store;
    private readonly Channel<float[]> _blocks = Channel.CreateUnbounded<float[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly Task _writer;
    private readonly object _gate = new();

    private float[] _block = new float[BlockSamples];
    private int _filled;
    private long _samples;
    private bool _completed;

    private TakeAudioRecorder(TakeAudioRecoveryStore store)
    {
        _store = store;
        _writer = Task.Run(WriteAsync);
    }

    /// <summary>
    /// Starts a fresh file for the take and the writer behind it. Null when the
    /// disk refuses (the store has logged why): the take then goes on without
    /// its audio copy.
    /// </summary>
    public static TakeAudioRecorder? Start(TakeAudioRecoveryStore store)
        => store.TryBegin() ? new TakeAudioRecorder(store) : null;

    /// <summary>
    /// Copies <paramref name="samples"/> into the current block. Ignored once
    /// the recorder is completed, so a late frame can never write to a file the
    /// engine has already judged.
    /// </summary>
    public void Push(ReadOnlyMemory<float> samples)
    {
        lock (_gate)
        {
            if (_completed) return;

            ReadOnlySpan<float> remaining = samples.Span;
            _samples += remaining.Length;
            while (!remaining.IsEmpty)
            {
                int take = Math.Min(remaining.Length, BlockSamples - _filled);
                remaining[..take].CopyTo(_block.AsSpan(_filled));
                _filled += take;
                remaining = remaining[take..];

                if (_filled == BlockSamples)
                {
                    _blocks.Writer.TryWrite(_block);
                    _block = new float[BlockSamples];
                    _filled = 0;
                }
            }
        }
    }

    /// <summary>
    /// Flushes the partial block, waits for the writer to write everything it
    /// was given and closes the file handle. Idempotent. Returns how many
    /// seconds of audio were pushed (what reached the disk, unless a write
    /// failed, which the store has logged).
    /// </summary>
    public double Complete()
    {
        long samples;
        lock (_gate)
        {
            if (!_completed)
            {
                _completed = true;
                if (_filled > 0)
                {
                    _blocks.Writer.TryWrite(_block.AsSpan(0, _filled).ToArray());
                    _filled = 0;
                }
                _blocks.Writer.Complete();
            }
            samples = _samples;
        }

        // Outside the lock: the writer never takes it, but waiting under it would
        // stall a late Push for no reason.
        try { _writer.Wait(DrainTimeout); }
        catch (AggregateException ex)
        {
            // The store never throws, so this is not expected; the take's end is
            // what matters, and the file keeps what was written.
            DeckleWhispSource.Log.TeardownExceptionIgnored(
                "take_audio_writer", ex.InnerException?.GetType().Name ?? ex.GetType().Name, ex.Message);
        }
        _store.Close();
        return samples / (double)TakeAudioRecoveryStore.SampleRate;
    }

    private async Task WriteAsync()
    {
        await foreach (float[] block in _blocks.Reader.ReadAllAsync().ConfigureAwait(false))
            _store.TryAppend(block);
    }
}
