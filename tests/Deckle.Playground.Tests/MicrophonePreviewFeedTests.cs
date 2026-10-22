using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Deckle.Audio;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Playground.Tests;

// The feed hands the HUD preview what a dictation hands the live HUD, and it
// must open the microphone only while the preview runs. These tests drive it
// through a fake capture, so no device is opened: they hold the feed to the
// segmenter a dictation uses, to what it asks the capture for, and to the
// promise a caller leans on, that once Stop returns nothing more arrives.
[Trait("Category", "unit")]
public sealed class MicrophonePreviewFeedTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private const float Voiced = 0.1f; // well above the -45 dBFS threshold
    private const float Silent = 0f;

    private static EnergySegmenterSettings TestSettings()
        => new()
        {
            HangoverMaxMs       = 400,
            HangoverMinMs       = 400,
            HangoverRampStartMs = 1_000_000,
            HangoverRampEndMs   = 1_000_000,
        };

    [Fact]
    public void StartingPublishesAnActiveGaugeBeforeAnyLevel()
    {
        using var h = new Harness();

        h.Feed.Start();

        Assert.Equal(new UtteranceProgressMeter(TestSettings()).Progress, Assert.Single(h.Progress));
        Assert.Empty(h.Levels);
    }

    [Fact]
    public void EachLevelReachesTheLevelAndTheGaugeTheDictationWouldShow()
    {
        using var h = new Harness();
        h.Feed.Start();
        var capture = h.WaitForCapture(0);

        var meter = new UtteranceProgressMeter(TestSettings());
        var levels = Enumerable.Repeat(Voiced, 8).Concat(Enumerable.Repeat(Silent, 6)).ToArray();
        var expected = new List<UtteranceProgress> { meter.Progress };
        foreach (float rms in levels)
        {
            capture.Raise(rms);
            expected.Add(meter.Push(rms));
        }

        Assert.Equal(levels, h.Levels.ToArray());
        Assert.Equal(expected, h.Progress.ToArray());
        // The sequence is not vacuous: the pause reached the gauge.
        Assert.Contains(expected, p => p.IsPausing && p.Fraction > 0f);
    }

    [Fact]
    public void ItOpensTheConfiguredDeviceAndKeepsNoAudio()
    {
        using var h = new Harness { Device = 7 };
        h.Feed.Start();
        var capture = h.WaitForCapture(0);

        var host = Assert.IsAssignableFrom<IAudioRecordingHost>(capture.Host);
        Assert.Equal(7, host.AudioInputDeviceId);
        Assert.False(host.RetainsAudio);
        Assert.Equal(0, host.MaxRecordingDurationSeconds);
        Assert.False(host.MicrophoneTelemetryEnabled);
        // A preview must not read as a dictation in the log.
        Assert.NotEqual(((IAudioRecordingHost)new DefaultHost()).RecordingPurpose, host.RecordingPurpose);
    }

    [Fact]
    public void ItDoesNotAskForTheFrameStream()
    {
        using var h = new Harness();
        h.Feed.Start();
        var capture = h.WaitForCapture(0);

        // Unsubscribed, the capture converts no float buffer: the level it
        // raises is already the RMS the preview needs.
        Assert.Equal(0, capture.FrameSubscribers);
    }

    [Fact]
    public void AfterStopReturnsNoLevelReachesASink()
    {
        using var h = new Harness();
        var capture = h.NextCapture(holdDevice: true);
        h.Feed.Start();
        h.WaitForCapture(0);
        capture.Raise(Voiced);

        h.Feed.Stop();
        int levels = h.Levels.Count;
        int progress = h.Progress.Count;

        // The capture thread is still inside Record, as it is while waveIn
        // drains, and keeps raising what it had.
        capture.Raise(Voiced);
        capture.Raise(Voiced);

        Assert.Equal(levels, h.Levels.Count);
        Assert.Equal(progress, h.Progress.Count);
        Assert.Equal(default, h.Progress.Last());
        Assert.True(capture.Token.IsCancellationRequested);
        capture.ReleaseDevice();
    }

    [Fact]
    public void AStoppedRunReportsNoFailureEvenWhenItsCaptureFailsAfterwards()
    {
        using var h = new Harness();
        var first = h.NextCapture(holdDevice: true);
        first.FailWith = 4;
        h.Feed.Start();
        h.WaitForCapture(0);
        h.Feed.Stop();
        first.ReleaseDevice();

        // The second run opens only once the first thread is done, so reaching
        // its Record means everything the first run would have said was said.
        h.Feed.Start();
        h.WaitForCapture(1);

        Assert.Empty(h.Failures);
    }

    [Theory]
    [InlineData(2u, "not-detected")]
    [InlineData(6u, "not-detected")]
    [InlineData(4u, "in-use")]
    [InlineData(7u, "unavailable")]
    public void AFailedOpenIsReportedWithItsKindAndCode(uint code, string cause)
    {
        using var h = new Harness();
        h.NextCapture().FailWith = code;

        h.Feed.Start();
        var failure = h.WaitForFailure();

        Assert.Equal(cause, failure.Cause);
        Assert.Equal(code, failure.MmsysErr);
        Assert.Equal(default, h.Progress.Last());
    }

    [Fact]
    public void ACaptureThatThrowsIsReportedByExceptionTypeOnly()
    {
        using var h = new Harness();
        h.NextCapture().Throws = new InvalidOperationException("device name: private");

        h.Feed.Start();
        var failure = h.WaitForFailure();

        Assert.Equal("exception", failure.Cause);
        Assert.Equal(nameof(InvalidOperationException), failure.ExceptionType);
        Assert.Equal(0u, failure.MmsysErr);
        Assert.DoesNotContain("private", failure.ToString());
        Assert.Equal(default, h.Progress.Last());
    }

    [Fact]
    public void ACaptureThatEndsOnItsOwnIsReportedAsEnded()
    {
        using var h = new Harness();
        h.NextCapture().EndsByItself = true;

        h.Feed.Start();
        var failure = h.WaitForFailure();

        Assert.Equal("ended", failure.Cause);
        Assert.Equal(default, h.Progress.Last());
    }

    [Fact]
    public void StartingAgainWhileOpenRestartsTheTakeOnTheSameCapture()
    {
        using var h = new Harness();
        h.Feed.Start();
        var capture = h.WaitForCapture(0);

        // A take that reached a cut...
        for (int i = 0; i < 10; i++) capture.Raise(Voiced);
        for (int i = 0; i < 10; i++) capture.Raise(Silent);
        Assert.Equal(1, h.Progress.Last().CompletedUtterances);

        h.Feed.Start();

        // ...starts over, without closing and reopening the device.
        Assert.Equal(new UtteranceProgressMeter(TestSettings()).Progress, h.Progress.Last());
        capture.Raise(Voiced);
        Assert.Equal(0, h.Progress.Last().CompletedUtterances);
        Assert.Equal(1, h.CaptureCount);
    }

    [Fact]
    public void ARestartAfterStopWaitsForTheDeviceToBeReleased()
    {
        using var h = new Harness();
        var first = h.NextCapture(holdDevice: true);
        h.Feed.Start();
        h.WaitForCapture(0);
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

        Assert.Empty(h.Progress);
        Assert.Equal(0, h.CaptureCount);
    }

    // The fake stands where waveIn stands: Record blocks on the recording
    // thread, raises levels there, and returns once the token fires and the
    // device is let go.
    private sealed class FakeCapture : IMicrophoneCapture
    {
        private readonly ManualResetEventSlim _deviceHeld = new(initialState: false);
        private readonly bool _holdDevice;

        public FakeCapture(bool holdDevice)
        {
            _holdDevice = holdDevice;
            if (!holdDevice) _deviceHeld.Set();
        }

        public event Action<float>? AudioLevel;
        public event Action<CaptureFrame>? Frame
        {
            add => Interlocked.Increment(ref _frameSubscribers);
            remove => Interlocked.Decrement(ref _frameSubscribers);
        }
        public event Action? CaptureStarted { add { } remove { } }
        public event Action? LowAudioDetected { add { } remove { } }

        private int _frameSubscribers;
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

        public ProbeResult Probe(int deviceId) => new(true, MicErrorKind.None, 0);

        public CaptureResult Record(IAudioRecordingHost host, CancellationToken cancellationToken)
        {
            Host = host;
            Token = cancellationToken;
            Recording.Set();

            if (Throws is { } ex) throw ex;
            if (FailWith != 0 && !_holdDevice) return Result(CaptureOutcome.MicError, FailWith);
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

    // The defaults a dictation's host carries, to compare the purpose against.
    private sealed class DefaultHost : IAudioRecordingHost
    {
        public int AudioInputDeviceId => 0;
        public int MaxRecordingDurationSeconds => 0;
        public bool MicrophoneTelemetryEnabled => false;
    }

    private sealed class Harness : IDisposable
    {
        private readonly object _lock = new();
        private readonly List<FakeCapture> _captures = new();
        private readonly Queue<FakeCapture> _planned = new();
        private readonly SemaphoreSlim _failureSignal = new(0);

        public ConcurrentQueue<float> Levels { get; } = new();
        public ConcurrentQueue<UtteranceProgress> Progress { get; } = new();
        public ConcurrentQueue<MicrophonePreviewFailure> Failures { get; } = new();
        public int Device { get; set; } = 1;
        public MicrophonePreviewFeed Feed { get; }

        public Harness()
        {
            Feed = new MicrophonePreviewFeed(
                createCapture: CreateCapture,
                deviceId: () => Device,
                segmenterSettings: TestSettings,
                onLevel: Levels.Enqueue,
                onProgress: Progress.Enqueue,
                onFailed: failure =>
                {
                    Failures.Enqueue(failure);
                    _failureSignal.Release();
                });
        }

        // Plans the next capture the feed will create, so a test can set it up.
        public FakeCapture NextCapture(bool holdDevice = false)
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

        public FakeCapture WaitForCapture(int index)
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
                    return capture.Recording.Wait(deadline - DateTime.UtcNow > TimeSpan.Zero
                        ? deadline - DateTime.UtcNow
                        : TimeSpan.Zero);
                Thread.Sleep(5);
            }
            return false;
        }

        public MicrophonePreviewFailure WaitForFailure()
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
