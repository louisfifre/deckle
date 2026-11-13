using System.Collections.Concurrent;
using Xunit;

namespace Deckle.Audio.Tests;

// The live microphone behind the Recording page's voice meter.
// Driven through a fake capture, so no device is opened: the tests hold the
// feed to what it asks the capture for (the caller's host, no Frame stream),
// to the promise its callers lean on, that once Stop returns nothing more
// arrives, to its wait for the device between two runs, and to the reason it
// gives when the microphone closes on its own.
[Trait("Category", "unit")]
public sealed class MicrophoneLevelFeedTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly double VoiceLevel = new CaptureSettings().VoiceLevelDbfs;

    [Fact]
    public void ItOpensTheCallersHostWithoutTheFrameStream()
    {
        using var h = new Harness();
        var host = new VoiceLevelMeterHost("{0.0.1.00000000}.{mic-7}", VoiceLevel);
        h.Host = host;

        Assert.True(h.Feed.Start());
        var capture = h.WaitForCapture();

        Assert.Same(host, capture.Host);
        // Unsubscribed, the capture converts no float buffer: the level it
        // raises is already the RMS the callers need.
        Assert.Equal(0, capture.FrameSubscribers);
    }

    [Fact]
    public void EachLevelReachesTheSinkUntilStopReturns()
    {
        using var h = new Harness();
        var capture = h.Plan(holdDevice: true);
        h.Feed.Start();
        h.WaitForCapture();
        capture.Raise(0.1f);
        capture.Raise(0.2f);

        h.Feed.Stop();
        // The capture thread is still inside Record, as while the device drains.
        capture.Raise(0.3f);

        Assert.Equal(new[] { 0.1f, 0.2f }, h.Levels.ToArray());
        Assert.True(capture.Token.IsCancellationRequested);
        capture.ReleaseDevice();
    }

    [Fact]
    public void OpenedComesBeforeTheFirstLevelAndClosedOnceAtStop()
    {
        using var h = new Harness();
        var capture = h.Plan(holdDevice: true);
        h.Feed.Start();
        h.WaitForCapture();
        capture.Raise(0.1f);

        h.Feed.Stop();
        h.Feed.Stop();
        capture.ReleaseDevice();

        Assert.Equal(new[] { "opened", "level", "closed" }, h.Events.ToArray());
    }

    [Fact]
    public void StartingWhileOpenDoesNothing()
    {
        using var h = new Harness();
        h.Feed.Start();
        h.WaitForCapture();

        Assert.False(h.Feed.Start());
        Assert.Equal(1, h.CaptureCount);
    }

    [Theory]
    [InlineData(0x80070490u, MicrophoneProblem.NotDetected)]
    [InlineData(0x88890004u, MicrophoneProblem.NotDetected)]
    [InlineData(0x8889000Au, MicrophoneProblem.InUse)]
    [InlineData(0x80004005u, MicrophoneProblem.Unavailable)]
    public void AFailedOpenIsReportedWithItsKindAndCode(uint code, MicrophoneProblem expected)
    {
        using var h = new Harness();
        h.Plan().FailWith = code;

        h.Feed.Start();
        var failure = h.WaitForFailure();

        Assert.Equal(expected, failure.Problem);
        Assert.Equal(code, failure.ErrorCode);
        Assert.False(h.Feed.IsRunning);
        // The run closed before its failure was reported.
        Assert.Equal(new[] { "opened", "closed", "failed" }, h.Events.ToArray());
    }

    [Fact]
    public void ACaptureThatEndsOnItsOwnIsReportedAsEnded()
    {
        using var h = new Harness();
        h.Plan().EndsByItself = true;

        h.Feed.Start();

        Assert.Equal(MicrophoneProblem.Ended, h.WaitForFailure().Problem);
    }

    [Fact]
    public void ACaptureThatThrowsIsReportedByExceptionTypeOnly()
    {
        using var h = new Harness();
        h.Plan().Throws = new InvalidOperationException("device name: private");

        h.Feed.Start();
        var failure = h.WaitForFailure();

        Assert.Equal(MicrophoneProblem.Faulted, failure.Problem);
        Assert.Equal(nameof(InvalidOperationException), failure.ExceptionType);
        Assert.Equal(0u, failure.ErrorCode);
        Assert.DoesNotContain("private", failure.ToString());
    }

    [Fact]
    public void AStoppedRunReportsNoFailure()
    {
        using var h = new Harness();
        var first = h.Plan(holdDevice: true);
        first.FailWith = 4;
        h.Feed.Start();
        h.WaitForCapture();
        h.Feed.Stop();
        first.ReleaseDevice();

        // The second run opens only once the first thread is done, so reaching
        // its Record means the first run has said all it would say.
        h.Feed.Start();
        h.WaitForCapture(1);

        Assert.Empty(h.Failures);
    }

    [Fact]
    public void ARestartAfterStopWaitsForTheDeviceToBeReleased()
    {
        using var h = new Harness();
        var first = h.Plan(holdDevice: true);
        h.Feed.Start();
        h.WaitForCapture();
        h.Feed.Stop();

        h.Feed.Start();

        // The first take still holds the device: the second must not ask for it.
        Assert.Equal(1, h.CaptureCount);
        Assert.False(h.TryWaitForCapture(1, TimeSpan.FromMilliseconds(200)));

        first.ReleaseDevice();
        h.WaitForCapture(1);
        Assert.True(first.Disposed);
    }

    [Fact]
    public void StoppingWithNothingOpenDoesNothing()
    {
        using var h = new Harness();

        h.Feed.Stop();

        Assert.Empty(h.Events);
        Assert.Equal(0, h.CaptureCount);
    }

    // A log line filters on the cause, so each problem has its own spelling.
    [Fact]
    public void EachProblemHasItsOwnLogCause()
    {
        var causes = Enum.GetValues<MicrophoneProblem>()
            .Select(problem => new MicrophoneLevelFeedFailure(problem).Cause)
            .ToArray();

        Assert.All(causes, cause => Assert.False(string.IsNullOrWhiteSpace(cause)));
        Assert.Equal(causes.Length, causes.Distinct().Count());
    }

    // The voice meter's host: the given device and level, nothing kept, and
    // its own purpose, so a meter left on does not read as a dictation.
    [Fact]
    public void TheVoiceMeterHostKeepsNothingAndNamesItsPurpose()
    {
        IAudioRecordingHost host = new VoiceLevelMeterHost("{0.0.1.00000000}.{mic-7}", VoiceLevel);

        Assert.Equal("{0.0.1.00000000}.{mic-7}", host.AudioInputEndpointId);
        Assert.Equal(VoiceLevel, host.VoiceLevelDbfs);
        Assert.False(host.RetainsAudio);
        Assert.Equal(0, host.MaxRecordingDurationSeconds);
        Assert.False(host.MicrophoneTelemetryEnabled);
        Assert.Equal("voice_meter", host.RecordingPurpose);
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
        public bool Disposed { get; private set; }
        public uint FailWith { get; set; }
        public Exception? Throws { get; set; }
        public bool EndsByItself { get; set; }

        public void Raise(float rms) => AudioLevel?.Invoke(rms);
        public void ReleaseDevice() => _deviceHeld.Set();

        public ProbeResult Probe(string endpointId) => new(true, MicErrorKind.None, 0);

        public CaptureResult Record(IAudioRecordingHost host, CancellationToken cancellationToken)
        {
            Host = host;
            Token = cancellationToken;
            Recording.Set();

            if (Throws is { } ex) throw ex;
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

        public void Dispose() => Disposed = true;
    }

    private sealed class Harness : IDisposable
    {
        private readonly object _lock = new();
        private readonly List<FakeCapture> _captures = new();
        private readonly Queue<FakeCapture> _planned = new();
        private readonly SemaphoreSlim _failureSignal = new(0);

        public ConcurrentQueue<float> Levels { get; } = new();
        public ConcurrentQueue<string> Events { get; } = new();
        public ConcurrentQueue<MicrophoneLevelFeedFailure> Failures { get; } = new();
        public IAudioRecordingHost Host { get; set; } = new VoiceLevelMeterHost("", VoiceLevel);
        public MicrophoneLevelFeed Feed { get; }

        public Harness() => Feed = new MicrophoneLevelFeed(
            createCapture: CreateCapture,
            host: () => Host,
            onLevel: rms =>
            {
                Levels.Enqueue(rms);
                Events.Enqueue("level");
            },
            onFailed: failure =>
            {
                Failures.Enqueue(failure);
                Events.Enqueue("failed");
                _failureSignal.Release();
            },
            onOpened: () => Events.Enqueue("opened"),
            onClosed: () => Events.Enqueue("closed"));

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

        public int CaptureCount { get { lock (_lock) return _captures.Count; } }

        public FakeCapture WaitForCapture(int index = 0)
        {
            Assert.True(TryWaitForCapture(index, Wait), $"capture {index} never reached Record");
            lock (_lock) return _captures[index];
        }

        public bool TryWaitForCapture(int index, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                FakeCapture? capture = null;
                lock (_lock) if (_captures.Count > index) capture = _captures[index];
                if (capture is not null)
                {
                    var left = deadline - DateTime.UtcNow;
                    return capture.Recording.Wait(left > TimeSpan.Zero ? left : TimeSpan.Zero);
                }
                Thread.Sleep(5);
            }
            return false;
        }

        public MicrophoneLevelFeedFailure WaitForFailure()
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
