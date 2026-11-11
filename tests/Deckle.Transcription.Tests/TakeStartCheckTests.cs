using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Deckle.Transcription;
using Xunit;
using Deckle.Localization;

namespace Deckle.Transcription.Tests;

// Every take opens with a test inference, concurrent with the capture (the
// prime). A first failure is retried once from a freshly loaded model, without
// a word; a second failure ends the take, or the file batch, with one message.
// The conditions of the bug are scripted here: a backend that keeps reporting
// its model as loaded after a native failure, as WhisperBackend does.
//
// In the observability collection because the tests listen to the provider
// process-wide.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "regression")]
public sealed class TakeStartCheckTests
{
    private const string Provider = "Deckle-Whisp";

    // A throw is the SEHException case; a bad result is the native code case.
    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    private static EventWrittenEventArgs[] Named(TestEventListener listener, string name)
        => listener.Events.Where(e => e.EventName == name).ToArray();

    // The messages the person was shown at the HUD's main surface.
    private static EventWrittenEventArgs[] Replacements(TestEventListener listener)
        => Named(listener, nameof(DeckleWhispSource.UserFeedbackEmitted))
            .Where(e => Field(e, "role") == "0")
            .ToArray();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFailedTestIsRetriedOnceFromAReloadedModelAndTheTakeGoesOn(bool throwInsteadOfBadResult)
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using var listener = new TestEventListener(Provider);
        var backend = new FlakyBackend(failingCalls: 1, throwInsteadOfBadResult, ["bonjour", "Deckle"]);
        var clipboard = new LiveTranscriptionPipelineTests.RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new LiveTranscriptionPipelineTests.StreamingHost(),
            backend,
            new LiveTranscriptionPipelineTests.ScriptedMicrophoneCapture(utteranceCount: 2),
            clipboard,
            captureTail: LiveTranscriptionPipelineTests.TestCaptureTail);
        var finished = NewFinishedSignal(engine);
        Task ready = NewReadySignal(engine);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
        await backend.AllUtterancesServed.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
        Assert.Equal(ToggleResult.Stopped, engine.RequestToggle(shouldPaste: false));
        Assert.Equal(
            TranscriptionOutcome.ClipboardOnly,
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation));
        await ready.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        // The model was unloaded through the backend and loaded again, once.
        Assert.Equal(1, backend.UnloadCount);
        Assert.Equal(1, backend.LoadCount);
        Assert.Equal("bonjour\n\nDeckle", clipboard.Text);

        // Silent: the person was told nothing, and the failure is in the log
        // with the memory at that moment.
        Assert.Empty(Named(listener, nameof(DeckleWhispSource.UserFeedbackEmitted)));
        EventWrittenEventArgs failed = Assert.Single(
            Named(listener, nameof(DeckleWhispSource.WarmupTestFailed)));
        Assert.Equal(EventLevel.Warning, failed.Level);
        Assert.Equal("1", Field(failed, "attempt"));
        Assert.Equal(throwInsteadOfBadResult ? "exception" : "bad_result", Field(failed, "reason"));
        Assert.Equal(throwInsteadOfBadResult ? "InvalidOperationException" : "", Field(failed, "ex_type"));
        Assert.Equal(throwInsteadOfBadResult ? "0" : "-1", Field(failed, "result_code"));
        Assert.True(long.Parse(Field(failed, "total_phys_mb")) > 0);
        Assert.True(long.Parse(Field(failed, "avail_phys_mb")) > 0);
        Assert.True(long.Parse(Field(failed, "commit_limit_mb")) > 0);
        Assert.Single(Named(listener, nameof(DeckleWhispSource.WarmupComplete)));
        Assert.Empty(Named(listener, nameof(DeckleWhispSource.WarmupFailed)));

        EventWrittenEventArgs ended = Assert.Single(Named(listener, nameof(DeckleWhispSource.TakeEnded)));
        Assert.Equal(TakeEndReasons.Delivered, Field(ended, "reason"));
    }

    // 2026-11-02: the prime threw while another process held 38 GB. The
    // recording went on for five minutes into a channel nobody read, then Stop
    // reported a crash and dropped everything. Now the second failure ends the
    // take by itself.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ATestThatFailsTwiceStopsTheTakeAtOnceWithOneMessage(bool throwInsteadOfBadResult)
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using var listener = new TestEventListener(Provider);
        var backend = new FlakyBackend(failingCalls: 2, throwInsteadOfBadResult, ["bonjour", "Deckle"]);
        var capture = new LiveTranscriptionPipelineTests.ScriptedMicrophoneCapture(utteranceCount: 2);
        var clipboard = new LiveTranscriptionPipelineTests.RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new LiveTranscriptionPipelineTests.StreamingHost(),
            backend,
            capture,
            clipboard,
            captureTail: LiveTranscriptionPipelineTests.TestCaptureTail);
        var finished = NewFinishedSignal(engine);
        Task ready = NewReadySignal(engine);

        long started = Stopwatch.GetTimestamp();
        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));

        // No Stop press: the engine ends the take on its own.
        Assert.Equal(
            TranscriptionOutcome.None,
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(4), testCancellation));
        await ready.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        // The capture token was cancelled by the engine, long before the
        // 5 s the scripted microphone would have waited for a manual stop.
        Assert.True(capture.RecordToken.IsCancellationRequested);
        TimeSpan untilCaptureStopped = Stopwatch.GetElapsedTime(started, capture.StopObservedTimestamp);
        Assert.True(
            untilCaptureStopped < TimeSpan.FromSeconds(3),
            $"The capture stopped {untilCaptureStopped.TotalMilliseconds:F0} ms after the start.");

        // Two tests, one unload and one reload between them, nothing delivered.
        Assert.Equal(2, backend.CallCount);
        Assert.Equal(1, backend.UnloadCount);
        Assert.Equal(1, backend.LoadCount);
        Assert.Equal(0, clipboard.WriteCount);

        // One message, on the main surface, as an error.
        EventWrittenEventArgs message = Assert.Single(Replacements(listener));
        Assert.Equal("2", Field(message, "severity"));
        Assert.Equal(Loc.Get("Engine_TranscriptionUnavailable_Title"), Field(message, "title"));
        Assert.Equal(Loc.Get("Engine_TranscriptionUnavailable_Body"), Field(message, "body"));
        Assert.Single(Named(listener, nameof(DeckleWhispSource.UserFeedbackEmitted)));

        // The take ends as a failure with its own reason, not as a crash.
        EventWrittenEventArgs ended = Assert.Single(Named(listener, nameof(DeckleWhispSource.TakeEnded)));
        Assert.Equal(TakeOutcomes.Failed, Field(ended, "outcome"));
        Assert.Equal(TakeEndReasons.TranscriptionUnavailable, Field(ended, "reason"));
        Assert.Empty(Named(listener, nameof(DeckleWhispSource.PipelineCrashed)));

        // Both attempts and the end of the warm-up are in the log.
        EventWrittenEventArgs[] attempts = Named(listener, nameof(DeckleWhispSource.WarmupTestFailed));
        Assert.Equal(["1", "2"], attempts.Select(e => Field(e, "attempt")).ToArray());
        EventWrittenEventArgs warmupFailed = Assert.Single(
            Named(listener, nameof(DeckleWhispSource.WarmupFailed)));
        Assert.Equal("test_failed", Field(warmupFailed, "reason"));
    }

    // A user Stop that lands before the second test has failed must not turn
    // the failed take into an empty one that says nothing.
    [Fact]
    public async Task AStopPressedBeforeTheSecondTestFailsStillEndsWithTheMessage()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using var listener = new TestEventListener(Provider);
        var backend = new FlakyBackend(failingCalls: 2, throwInsteadOfBadResult: true, ["bonjour"])
        {
            HoldSecondFailure = new ManualResetEventSlim(false),
        };
        using var engine = new TranscriptionEngine(
            new LiveTranscriptionPipelineTests.StreamingHost(),
            backend,
            new LiveTranscriptionPipelineTests.ScriptedMicrophoneCapture(utteranceCount: 1),
            new LiveTranscriptionPipelineTests.RecordingClipboardWriter(),
            captureTail: LiveTranscriptionPipelineTests.TestCaptureTail);
        var finished = NewFinishedSignal(engine);
        Task ready = NewReadySignal(engine);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
        await backend.SecondTestReached.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
        Assert.Equal(ToggleResult.Stopped, engine.RequestToggle(shouldPaste: false));
        backend.HoldSecondFailure.Set();

        Assert.Equal(
            TranscriptionOutcome.None,
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation));
        await ready.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        Assert.Single(Replacements(listener));
        EventWrittenEventArgs ended = Assert.Single(Named(listener, nameof(DeckleWhispSource.TakeEnded)));
        Assert.Equal(TakeEndReasons.TranscriptionUnavailable, Field(ended, "reason"));
    }

    // The monolithic pipeline waits for the prime only at Stop.
    [Fact]
    public async Task TheMonolithicPipelineEndsWithTheSameMessageAndReason()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using var listener = new TestEventListener(Provider);
        var backend = new FlakyBackend(failingCalls: 2, throwInsteadOfBadResult: true, ["bonjour"]);
        var host = new LiveTranscriptionPipelineTests.StreamingHost();
        host.Transcription.Streaming.Strategy = PipelineStrategyKind.Monolithic;
        var clipboard = new LiveTranscriptionPipelineTests.RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            host,
            backend,
            new LiveTranscriptionPipelineTests.ScriptedMicrophoneCapture(utteranceCount: 1),
            clipboard,
            captureTail: LiveTranscriptionPipelineTests.TestCaptureTail);
        var finished = NewFinishedSignal(engine);
        Task ready = NewReadySignal(engine);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
        await backend.SecondFailureReturned.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
        Assert.Equal(ToggleResult.Stopped, engine.RequestToggle(shouldPaste: false));

        Assert.Equal(
            TranscriptionOutcome.None,
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation));
        await ready.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        Assert.Equal(2, backend.CallCount);
        Assert.Equal(0, clipboard.WriteCount);
        EventWrittenEventArgs message = Assert.Single(Replacements(listener));
        Assert.Equal(Loc.Get("Engine_TranscriptionUnavailable_Title"), Field(message, "title"));
        EventWrittenEventArgs ended = Assert.Single(Named(listener, nameof(DeckleWhispSource.TakeEnded)));
        Assert.Equal(TakeOutcomes.Failed, Field(ended, "outcome"));
        Assert.Equal(TakeEndReasons.TranscriptionUnavailable, Field(ended, "reason"));
        Assert.Empty(Named(listener, nameof(DeckleWhispSource.PipelineCrashed)));
    }

    // Before the fix a throwing prime failed the generic catch of every file, one
    // overlay each, with no retry.
    [Fact]
    public async Task AFileBatchWhoseTestFailsTwiceStopsAfterTheFirstFileWithOneMessage()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        string directory = Path.Combine(Path.GetTempPath(), $"deckle-take-start-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string[] files = [WriteWav(directory, "first"), WriteWav(directory, "second"), WriteWav(directory, "third")];
            using var listener = new TestEventListener(Provider);
            var backend = new FlakyBackend(failingCalls: 2, throwInsteadOfBadResult: true, []);
            using var engine = new TranscriptionEngine(
                new LiveTranscriptionPipelineTests.StreamingHost(), backend);
            var finished = NewFinishedSignal(engine);
            Task ready = NewReadySignal(engine);
            int finishes = 0;
            engine.TranscriptionFinished += _ => Interlocked.Increment(ref finishes);

            Assert.Equal(3, engine.EnqueueFileTranscriptions(files));
            Assert.Equal(
                TranscriptionOutcome.None,
                await finished.Task.WaitAsync(TimeSpan.FromSeconds(15), testCancellation));
            await ready.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

            // One message for the whole batch, however many files were left.
            EventWrittenEventArgs message = Assert.Single(
                Named(listener, nameof(DeckleWhispSource.UserFeedbackEmitted)));
            Assert.Equal("0", Field(message, "role"));
            Assert.Equal(Loc.Get("Engine_TranscriptionUnavailable_Title"), Field(message, "title"));
            Assert.Single(Named(listener, nameof(DeckleWhispSource.FileTranscriptionFailed)));
            EventWrittenEventArgs ended = Assert.Single(Named(listener, nameof(DeckleWhispSource.TakeEnded)));
            Assert.Equal(TakeEndReasons.TranscriptionUnavailable, Field(ended, "reason"));
            Assert.Equal(1, Volatile.Read(ref finishes));
            EventWrittenEventArgs aborted = Assert.Single(
                Named(listener, nameof(DeckleWhispSource.FileTranscriptionBatchAborted)));
            Assert.Equal("failed", Field(aborted, "outcome"));
            Assert.Empty(Named(listener, nameof(DeckleWhispSource.PipelineCrashed)));

            // No file was ever transcribed: the two tests are all the backend saw.
            Assert.Equal(2, backend.CallCount);
            Assert.False(engine.IsBusy);
            Assert.Empty(Directory.GetFiles(directory, "*.txt"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // The test inference opens the take a few milliseconds after the hotkey. The
    // idle timer holds the unload lock for the whole native free while the backend
    // still reports the model as loaded; a take started inside that window must
    // wait for the free and load the model again, not decode on a context being
    // freed. The timer callback has no seam, so the test calls the unload it runs.
    [Fact]
    public async Task ATakeStartedDuringAnIdleUnloadWaitsForTheFreeBeforeItsTest()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        var backend = new SlowUnloadBackend();
        using var engine = new TranscriptionEngine(
            new LiveTranscriptionPipelineTests.StreamingHost(),
            backend,
            new LiveTranscriptionPipelineTests.ScriptedMicrophoneCapture(utteranceCount: 0),
            new LiveTranscriptionPipelineTests.RecordingClipboardWriter(),
            captureTail: LiveTranscriptionPipelineTests.TestCaptureTail);
        var finished = NewFinishedSignal(engine);
        Task ready = NewReadySignal(engine);

        var idleUnload = typeof(TranscriptionEngine).GetMethod(
            "UnloadModel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Task unload = Task.Run(() => idleUnload.Invoke(engine, null), testCancellation);
        await backend.UnloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));

        // Room for a prime that did not wait to reach the backend.
        await Task.Delay(500, testCancellation);
        Assert.False(backend.TranscribedDuringUnload);
        Assert.Equal(0, backend.CallCount);

        backend.ReleaseUnload.Set();
        await unload.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
        await backend.Transcribed.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        Assert.Equal(ToggleResult.Stopped, engine.RequestToggle(shouldPaste: false));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
        await ready.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        Assert.False(backend.TranscribedDuringUnload);
        Assert.Equal(1, backend.LoadCount);
        Assert.Equal(1, backend.CallCount);
    }

    private static TaskCompletionSource<TranscriptionOutcome> NewFinishedSignal(TranscriptionEngine engine)
    {
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.TranscriptionFinished += outcome => finished.TrySetResult(outcome);
        return finished;
    }

    // Completes on the Ready the worker raises once it has settled.
    private static Task NewReadySignal(TranscriptionEngine engine)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StatusChanged += status =>
        {
            if (status == Loc.Get("Status_Ready")) ready.TrySetResult();
        };
        return ready.Task;
    }

    // One second of a quiet tone, as 16 kHz mono 16-bit PCM: a file Media
    // Foundation decodes, with audio in it.
    private static string WriteWav(string directory, string name)
    {
        const int rate = 16_000;
        const int samples = rate;
        string path = Path.Combine(directory, name + ".wav");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (int i = 0; i < samples; i++)
            writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 0.3 * short.MaxValue));
        return path;
    }

    // Holds its unload open until released, and keeps reporting the model as
    // loaded meanwhile, as WhisperBackend does until whisper_free has returned.
    private sealed class SlowUnloadBackend : IAsrBackend
    {
        private volatile bool _loaded = true;
        private volatile bool _unloading;
        private int _callCount;
        private int _loadCount;

        public TaskCompletionSource UnloadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Transcribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseUnload { get; } = new(false);
        public volatile bool TranscribedDuringUnload;

        public string Name => "slow-unload";
        public bool IsModelLoaded => _loaded;
        public string? DetectedAccelerator => "test";
        public int CallCount => Volatile.Read(ref _callCount);
        public int LoadCount => Volatile.Read(ref _loadCount);

        public Task<ModelLoadResult> LoadModelAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _loadCount);
            _loaded = true;
            return Task.FromResult(new ModelLoadResult(true, 0, "test", null));
        }

        public void UnloadModel()
        {
            _unloading = true;
            UnloadEntered.TrySetResult();
            ReleaseUnload.Wait(TimeSpan.FromSeconds(10));
            _loaded = false;
            _unloading = false;
        }

        public Task<TranscriptionResult> TranscribeAsync(
            ReadOnlyMemory<float> pcmSamples,
            Action<TranscriptionSegment>? segmentSink,
            CancellationToken ct,
            TranscriptionContext? context = null)
        {
            if (_unloading) TranscribedDuringUnload = true;
            Interlocked.Increment(ref _callCount);
            Transcribed.TrySetResult();
            return Task.FromResult(new TranscriptionResult([], "", 0, 0, false, 0));
        }

        public void Dispose() { }
    }

    // Fails its first calls the way the Whisper backend does under memory
    // pressure, and keeps reporting the model as loaded afterwards: only an
    // unload through the backend clears it. The calls after the failing ones are
    // the passing test, which returns nothing, then one per scripted utterance.
    private sealed class FlakyBackend : IAsrBackend
    {
        private readonly int _failingCalls;
        private readonly bool _throw;
        private readonly IReadOnlyList<string> _utterances;
        private volatile bool _loaded = true;
        private int _callCount;
        private int _unloadCount;
        private int _loadCount;

        public FlakyBackend(int failingCalls, bool throwInsteadOfBadResult, IReadOnlyList<string> utterances)
        {
            _failingCalls = failingCalls;
            _throw = throwInsteadOfBadResult;
            _utterances = utterances;
        }

        // When set, the second failing call waits for it before it fails.
        public ManualResetEventSlim? HoldSecondFailure { get; init; }

        public TaskCompletionSource AllUtterancesServed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondTestReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondFailureReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "flaky";
        public bool IsModelLoaded => _loaded;
        public string? DetectedAccelerator => "test";
        public int CallCount => Volatile.Read(ref _callCount);
        public int UnloadCount => Volatile.Read(ref _unloadCount);
        public int LoadCount => Volatile.Read(ref _loadCount);

        public Task<ModelLoadResult> LoadModelAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _loadCount);
            _loaded = true;
            return Task.FromResult(new ModelLoadResult(true, 0, "test", null));
        }

        public void UnloadModel()
        {
            Interlocked.Increment(ref _unloadCount);
            _loaded = false;
        }

        public Task<TranscriptionResult> TranscribeAsync(
            ReadOnlyMemory<float> pcmSamples,
            Action<TranscriptionSegment>? segmentSink,
            CancellationToken ct,
            TranscriptionContext? context = null)
        {
            int call = Interlocked.Increment(ref _callCount) - 1;

            if (call < _failingCalls)
            {
                if (call == 1)
                {
                    SecondTestReached.TrySetResult();
                    HoldSecondFailure?.Wait(TimeSpan.FromSeconds(5));
                }

                try
                {
                    if (_throw)
                        throw new InvalidOperationException("simulated native failure");
                    return Task.FromResult(new TranscriptionResult(
                        Array.Empty<TranscriptionSegment>(), "", 0, 0, false, -1, "native_error"));
                }
                finally
                {
                    if (call == 1) SecondFailureReturned.TrySetResult();
                }
            }

            int utterance = call - _failingCalls - 1;
            if (utterance < 0)
                return Task.FromResult(new TranscriptionResult([], "", 0, 0, false, 0));

            string text = _utterances[utterance];
            var segment = new TranscriptionSegment(text, 0, 10, 1f, 0f);
            segmentSink?.Invoke(segment);
            if (utterance == _utterances.Count - 1)
                AllUtterancesServed.TrySetResult();
            return Task.FromResult(new TranscriptionResult([segment], text, 1, 0, false, 0));
        }

        public void Dispose() { }
    }
}
