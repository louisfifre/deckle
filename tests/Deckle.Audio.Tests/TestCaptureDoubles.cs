using Deckle.Audio.Internal;

namespace Deckle.Audio.Tests;

// Test doubles shared by the capture-loop and microphone-capture tests: a
// scripted device behind ICaptureEndpoint, a provider that hands it out, and a
// recording host with settable values.
//
// The script is a queue of integers read by Available():
//   n > 0  n frames are ready; Read consumes them (a read may take part of them).
//   0      Available reports "nothing ready" once.
//   n < 0  Available reports that error code once (device lost).
// An exhausted script reports 0 forever, so a test ends the take with a token.
internal sealed class ScriptedCaptureEndpoint : ICaptureEndpoint
{
    private readonly List<int> _script;
    private readonly Func<long, float> _signal;
    private long _frameIndex;

    public ScriptedCaptureEndpoint(
        IEnumerable<int> script,
        CaptureFormat? format = null,
        Func<long, float>? signal = null,
        bool exclusive = false,
        CaptureMode mode = CaptureMode.Raw)
    {
        _script = [.. script];
        _signal = signal ?? (_ => 0.25f);
        Info = new CaptureEndpointInfo(
            new CaptureDeviceIdentity("{0.0.1.00000000}.{test}", "Test microphone", IsDefault: true),
            mode, exclusive, format ?? new CaptureFormat(48000, 1), InputLatencyMs: 10);
    }

    public CaptureEndpointInfo Info { get; }

    // Every Start, Read, Stop and Dispose, in call order.
    public List<string> Calls { get; } = [];

    public int StartCode { get; set; }
    public int StopCode { get; set; }

    // Runs after each successful Read, with the count of reads so far.
    public Action<int>? AfterRead { get; set; }

    public int ReadCount { get; private set; }

    public void Enqueue(params int[] items)
    {
        _script.AddRange(items);
    }

    public int Start()
    {
        Calls.Add("Start");
        return StartCode;
    }

    public int Stop()
    {
        Calls.Add("Stop");
        return StopCode;
    }

    public int Available()
    {
        if (_script.Count == 0) return 0;
        int head = _script[0];
        if (head > 0) return head;
        _script.RemoveAt(0);
        return head;
    }

    public int Read(Span<float> interleaved, int frames)
    {
        Calls.Add("Read");
        int ready = _script[0];
        if (frames > ready) throw new InvalidOperationException($"Read {frames} frames, {ready} were ready.");
        // A packet bigger than one read is consumed over several reads.
        if (frames == ready) _script.RemoveAt(0); else _script[0] = ready - frames;

        int channels = Info.Format.Channels;
        for (int f = 0; f < frames; f++)
        {
            float value = _signal(_frameIndex++);
            for (int c = 0; c < channels; c++) interleaved[f * channels + c] = value;
        }

        ReadCount++;
        AfterRead?.Invoke(ReadCount);
        return 0;
    }

    public void Dispose() => Calls.Add("Dispose");
}

internal sealed class FakeEndpointProvider(Func<string, CaptureOptions, CaptureOpenResult> open) : ICaptureEndpointProvider
{
    public List<(string EndpointId, CaptureOptions Options)> Requests { get; } = [];

    public CaptureOpenResult Open(string endpointId, CaptureOptions options)
    {
        Requests.Add((endpointId, options));
        return open(endpointId, options);
    }
}

internal sealed class FakeRecordingHost : IAudioRecordingHost
{
    public string AudioInputEndpointId { get; set; } = "";
    public int MaxRecordingDurationSeconds { get; set; }
    public bool MicrophoneTelemetryEnabled { get; set; }
    public double VoiceLevelDbfs { get; set; } = -40;
    public bool RetainsAudio { get; set; } = true;
    public bool ExclusiveMode { get; set; }
}

