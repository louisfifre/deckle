using System.Threading.Channels;
using Deckle.Audio;

namespace Deckle.Transcription;

// One source-neutral segmented transcription session. A producer pushes the
// same CaptureFrame contract whether audio comes from the microphone or a
// decoded file; EnergySegmenter owns the cut decisions and a single consumer
// drains the ordered utterances. The session owns no engine state or delivery.
internal sealed class SegmentedTranscriptionSession<TResult>
{
    private readonly Channel<Utterance> _utterances;
    private readonly EnergySegmenter _segmenter;
    private int _backlog;
    private bool _completed;

    public SegmentedTranscriptionSession(
        EnergySegmenterSettings settings,
        Func<ChannelReader<Utterance>, Func<int>, Task<TResult>> consume)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(consume);

        _utterances = Channel.CreateUnbounded<Utterance>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        _segmenter = new EnergySegmenter(settings, utterance =>
        {
            Interlocked.Increment(ref _backlog);
            if (!_utterances.Writer.TryWrite(utterance))
            {
                Interlocked.Decrement(ref _backlog);
                throw new InvalidOperationException("Cannot emit an utterance after the session completed.");
            }
        });

        Completion = consume(
            _utterances.Reader,
            () => Interlocked.Decrement(ref _backlog));
    }

    public Task<TResult> Completion { get; }

    public int Backlog => Volatile.Read(ref _backlog);

    public void Push(CaptureFrame frame)
    {
        if (_completed)
            throw new InvalidOperationException("Cannot push audio after the session completed.");

        _segmenter.Push(frame);
    }

    public SegmenterSnapshot Snapshot() => _segmenter.Snapshot();

    public void Complete()
    {
        if (_completed)
            return;

        _completed = true;
        try
        {
            _segmenter.Flush();
        }
        finally
        {
            _utterances.Writer.TryComplete();
        }
    }
}
