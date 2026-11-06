using System.Collections.Concurrent;
using Xunit;

namespace Deckle.Audio.Tests;

// The microphone behind the Recording page's voice meter. Driven through a fake
// capture, so no device is opened: the tests hold the feed to what it asks the
// capture for (nothing kept, no Frame stream, its own purpose in the log), to
// the promise the meter leans on, that once Stop returns no level arrives, and
// to the reason it gives when the microphone closes on its own.
[Trait("Category", "unit")]
public sealed class VoiceLevelMeterFeedTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly double VoiceLevel = new CaptureSettings().VoiceLevelDbfs;

    [Fact]
    public void ItOpensTheGivenDeviceAndKeepsNothing()
    {
        using var h = new Harness();
        h.Feed.Start(7, VoiceLevel, h.OnFailed);
        var capture = h.WaitForCapture();

        var host = Assert.IsAssignableFrom<IAudioRecordingHost>(capture.Host);
        Assert.Equal(7, host.AudioInputDeviceId);
        Assert.Equal(VoiceLevel, host.VoiceLevelDbfs);
        Assert.False(host.RetainsAudio);
        Assert.Equal(0, host.MaxRecordingDurationSeconds);
        Assert.False(host.MicrophoneTelemetryEnabled);
        Assert.Equal("voice_meter", host.RecordingPurpose);
        // Unsubscribed, the capture converts no float buffer.
        Assert.Equal(0, capture.FrameSubscribers);
    }

    [Fact]
    public void EachLevelReachesTheSinkUntilStopReturns()
    {
        using var h = new Harness();
        var capture = h.Plan(holdDevice: true);
        h.Feed.Start(0, VoiceLevel, h.OnFailed);
        h.WaitForCapture();
        capture.Raise(0.1f);
        capture.Raise(0.2f);

        h.Feed.Stop();
        // The capture thread is still inside Record, as while waveIn drains.
        capture.Raise(0.3f);

        Assert.Equal(new[] { 0.1f, 0.2f }, h.Levels.ToArray());
        Assert.True(capture.Token.IsCancellationRequested);
        capture.ReleaseDevice();
    }

    // The kind travels as an int: the enum is internal, and a public test
    // method cannot take it as a parameter.
    [Theory]
    [InlineData(2u, (int)VoiceLevelMeterFailure.NotDetected)]
    [InlineData(4u, (int)VoiceLevelMeterFailure.InUse)]
    [InlineData(7u, (int)VoiceLevelMeterFailure.Unavailable)]
    public void AFailedOpenIsReportedWithItsKind(uint code, int expected)
    {
        using var h = new Harness();
        h.Plan().FailWith = code;

        h.Feed.Start(0, VoiceLevel, h.OnFailed);

        Assert.Equal((VoiceLevelMeterFailure)expected, h.WaitForFailure());
        Assert.False(h.Feed.IsRunning);
    }

    [Fact]
    public void ACaptureThatEndsOnItsOwnIsReportedAsEnded()
    {
        using var h = new Harness();
        h.Plan().EndsByItself = true;

        h.Feed.Start(0, VoiceLevel, h.OnFailed);

        Assert.Equal(VoiceLevelMeterFailure.Ended, h.WaitForFailure());
    }

    [Fact]
    public void AStoppedRunReportsNoFailure()
    {
        using var h = new Harness();
        var first = h.Plan(holdDevice: true);
        first.FailWith = 4;
        h.Feed.Start(0, VoiceLevel, h.OnFailed);
        h.WaitForCapture();
        h.Feed.Stop();
        first.ReleaseDevice();

        // The second run opens only once the first thread is done, so reaching
        // its Record means the first run has said all it would say.
        h.Feed.Start(0, VoiceLevel, h.OnFailed);
        h.WaitForCapture(1);

        Assert.Empty(h.Failures);
    }

    // Record blocks on the capture thread, raises levels there, and returns
    // once the token fires and the device is let go.
    private sealed class FakeCapture(bool holdDevice) : IMicrophoneCapture
    {
        private readonly ManualResetEventSlim _deviceHeld = new(initialState: !holdDevice);
        private int _frameSubscribers;

        public event Action<float>? AudioLevel;
        public event Action<CaptureFrame>? Frame
        {
            add => Interlocked.Increment(ref _frameSubscribers);
            remove => Interlocked.Decrement(ref _frameSubscribers);
        }
        public event Action? CaptureStarted { add { } remove { } }
        public event Action? LowAudioDetected { add { } remove { } }

        public int FrameSubscribers => Volatile.Read(ref _frameSubscribers);
        public IAudioRecordingHost? Host { get; private set; }
        public CancellationToken Token { get; private set; }
        public ManualResetEventSlim Recording { get; } = new(initialState: false);
        public uint FailWith { get; set; }
        public bool EndsByItself { get; set; }

        public void Raise(float rms) => AudioLevel?.Invoke(rms);
        public void ReleaseDevice() => _deviceHeld.Set();

        public ProbeResult Probe(int deviceId) => new(true, MicErrorKind.None, 0);

        public CaptureResult Record(IAudioRecordingHost host, CancellationToken cancellationToken)
        {
            Host = host;
            Token = cancellationToken;
            Recording.Set();

            if (FailWith != 0 && !holdDevice) return Result(CaptureOutcome.MicError, FailWith);
            if (EndsByItself) return Result(CaptureOutcome.Completed, 0);

            cancellationToken.WaitHandle.WaitOne();
            _deviceHeld.Wait();
            return FailWith != 0
                ? Result(CaptureOutcome.MicError, FailWith)
                : Result(CaptureOutcome.Completed, 0);
        }

        private static CaptureResult Result(CaptureOutcome outcome, uint err)
            => new(Array.Empty<float>(), null, outcome, TimeSpan.Zero, err);

        public void Dispose() { }
    }

    private sealed class Harness : IDisposable
    {
        private readonly object _lock = new();
        private readonly List<FakeCapture> _captures = new();
        private readonly Queue<FakeCapture> _planned = new();
        private readonly SemaphoreSlim _failureSignal = new(0);

        public ConcurrentQueue<float> Levels { get; } = new();
        public ConcurrentQueue<VoiceLevelMeterFailure> Failures { get; } = new();
        public VoiceLevelMeterFeed Feed { get; }

        public Harness() => Feed = new VoiceLevelMeterFeed(CreateCapture, Levels.Enqueue);

        public void OnFailed(VoiceLevelMeterFailure failure)
        {
            Failures.Enqueue(failure);
            _failureSignal.Release();
        }

        public FakeCapture Plan(bool holdDevice = false)
        {
            var capture = new FakeCapture(holdDevice);
            lock (_lock) _planned.Enqueue(capture);
            return capture;
        }

        private IMicrophoneCapture CreateCapture()
        {
            lock (_lock)
            {
                var capture = _planned.Count > 0 ? _planned.Dequeue() : new FakeCapture(holdDevice: false);
                _captures.Add(capture);
                return capture;
            }
        }

        public FakeCapture WaitForCapture(int index = 0)
        {
            var deadline = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < deadline)
            {
                FakeCapture? capture = null;
                lock (_lock) if (_captures.Count > index) capture = _captures[index];
                if (capture is not null && capture.Recording.Wait(Wait)) return capture;
                Thread.Sleep(5);
            }
            Assert.Fail($"capture {index} never reached Record");
            return null!;
        }

        public VoiceLevelMeterFailure WaitForFailure()
        {
            Assert.True(_failureSignal.Wait(Wait), "no failure was reported");
            Assert.True(Failures.TryPeek(out var failure));
            return failure;
        }

        public void Dispose()
        {
            Feed.Stop();
            lock (_lock) foreach (var capture in _captures) capture.ReleaseDevice();
        }
    }
}
