namespace Deckle.Audio.Internal;

// The signal path of one take, from a device packet to everything the
// consumers read, in Deckle's own code from the first sample:
//
//   device packet (mix format, N channels)
//     -> MonoConverter        mean of the channels
//     -> StreamingResampler   to 16 kHz
//     -> level pulse          per packet, for what a person watches
//     -> FloatTakeBuffer      the take's audio
//     -> SubWindowAssembler   50 ms windows: RMS log, AudioLevel, Frame, low-audio tracker
//
// No gain, filter, gate or compressor sits on this path: the only operations
// are the channel mean and the sample-rate conversion, both defined by the
// source files beside it. What leaves it is what the device gave.
//
// It owns no thread and no device, so it is driven by CaptureLoop in production
// and directly by a test with synthetic packets.
internal sealed class CapturePipeline
{
    public const int OutputRate = SubWindowAssembler.SampleRate;

    private readonly CaptureFormat _format;
    private readonly StreamingResampler _resampler;
    private readonly SubWindowAssembler _windows = new();
    private readonly FloatTakeBuffer _take;
    private readonly List<float> _rmsLog;
    private readonly Action<float>? _audioLevel;
    private readonly Action<CaptureFrame>? _frame;
    private readonly Action<CaptureLevel>? _level;
    private readonly LowAudioTracker? _lowAudio;
    private readonly Action? _onLowAudio;
    private readonly SubWindowAssembler.WindowHandler _onWindow;

    private float[] _mono = new float[1024];

    public CapturePipeline(
        CaptureFormat format,
        bool retainAudio,
        List<float> rmsLog,
        Action<float>? audioLevel,
        Action<CaptureFrame>? frame,
        Action<CaptureLevel>? level,
        LowAudioTracker? lowAudio,
        Action? onLowAudio)
    {
        _format = format;
        _resampler = new StreamingResampler(format.SampleRate, OutputRate);
        _take = new FloatTakeBuffer(initialCapacity: OutputRate, retain: retainAudio);
        _rmsLog = rmsLog;
        _audioLevel = audioLevel;
        _frame = frame;
        _level = level;
        _lowAudio = lowAudio;
        _onLowAudio = onLowAudio;
        _onWindow = OnWindow;
    }

    // Samples of the take so far, at 16 kHz, retained or not.
    public long SampleCount => _take.Count;

    public double Seconds => _take.Count / (double)OutputRate;

    public float[] ToArray() => _take.ToArray();

    // One read from the device: frames interleaved frames in its own format.
    public void Accept(ReadOnlySpan<float> interleaved, int frames)
    {
        if (frames <= 0) return;

        if (_mono.Length < frames) _mono = new float[Math.Max(frames, _mono.Length * 2)];
        Span<float> mono = _mono.AsSpan(0, frames);
        MonoConverter.ToMono(interleaved, _format.Channels, frames, mono);

        Emit(_resampler.Process(mono));
    }

    // Ends the take's stream: the resampler's look-ahead is flushed, so the take
    // has exactly the length of the audio the device delivered.
    public void Finish() => Emit(_resampler.Flush());

    private void Emit(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;

        _take.Append(samples);
        _level?.Invoke(CaptureLevel.Of(samples, OutputRate));
        _windows.Push(samples, _onWindow);
    }

    // The sub-window walker: the RMS is appended to the log unconditionally, so
    // the Stop-time microphone telemetry covers the whole session even when
    // nothing else listens. The frame's samples are a fresh array, never an
    // alias of the assembler's, so a subscriber may keep them.
    private void OnWindow(ReadOnlySpan<float> window, float rms)
    {
        _rmsLog.Add(rms);
        _audioLevel?.Invoke(rms);
        _frame?.Invoke(new CaptureFrame(window.ToArray(), rms));

        if (_lowAudio is not null && _lowAudio.Observe(rms, SubWindowAssembler.WindowMs))
            _onLowAudio?.Invoke();
    }
}
