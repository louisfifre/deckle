using Deckle.Audio;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// Bug note — 2026-07-31
// Trigger: importing several files from the tray in one picker selection.
// Observed: the timer and status reset, with a visible pause between files.
// Cause: each file owned a separate worker and lifecycle instead of feeding the live segmented session.
// Failed invariant: one selection is one continuous lifecycle with one segmented consumer.
// Watch: any per-file Ready/Finished transition or separate ASR path reintroduces the stutter.
[Trait("Category", "regression")]
public sealed class FileBatchLifecycleTests
{
    [Fact]
    public async Task OneSelectionPublishesOneStartAndOneTerminalLifecycle()
    {
        using var engine = new TranscriptionEngine(new FakeHost(), new LoadedBackend());
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        int finishes = 0;
        int statuses = 0;

        engine.FileTranscriptionStarted += () => Interlocked.Increment(ref starts);
        engine.TranscriptionFinished += outcome =>
        {
            Interlocked.Increment(ref finishes);
            finished.TrySetResult(outcome);
        };
        engine.StatusChanged += status =>
        {
            Interlocked.Increment(ref statuses);
            ready.TrySetResult(status);
        };

        int accepted = engine.EnqueueFileTranscriptions(
            [MissingPath("first"), MissingPath("second")]);

        Assert.Equal(2, accepted);
        Assert.Equal(TranscriptionOutcome.None, await finished.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        await ready.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref starts));
        Assert.Equal(1, Volatile.Read(ref finishes));
        Assert.Equal(1, Volatile.Read(ref statuses));
        Assert.False(engine.IsBusy);
    }

    [Fact]
    public async Task DisposeFromStartSubscriberCannotReleaseALateWorker()
    {
        var backend = new LoadedBackend();
        var engine = new TranscriptionEngine(new FakeHost(), backend);
        int finishes = 0;
        engine.TranscriptionFinished += _ => Interlocked.Increment(ref finishes);
        engine.FileTranscriptionStarted += engine.Dispose;

        engine.EnqueueFileTranscriptions([MissingPath("disposed")]);
        await Task.Delay(
            TimeSpan.FromMilliseconds(250),
            TestContext.Current.CancellationToken);

        Assert.True(backend.Disposed);
        Assert.Equal(0, Volatile.Read(ref finishes));
        Assert.False(engine.IsBusy);
    }

    private static string MissingPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"deckle-{label}-{Guid.NewGuid():N}.wav");

    private sealed class FakeHost : ITranscriptionEngineHost
    {
        public TranscriptionSettings Transcription { get; } = new();
        public CaptureSettings Audio { get; } = new();
        public TelemetrySettings Telemetry { get; } = new();
        public LlmSettings Llm { get; } = new();
        public string ResolveModelsDirectory() => Path.GetTempPath();
        public void SaveSettings() { }
        public void ApplyLevelWindow(LevelWindowSettings lw) { }
    }

    private sealed class LoadedBackend : IAsrBackend
    {
        public string Name => "test";
        public bool IsModelLoaded => true;
        public string? DetectedAccelerator => "test";
        public bool Disposed { get; private set; }

        public Task<ModelLoadResult> LoadModelAsync(CancellationToken ct) =>
            Task.FromResult(new ModelLoadResult(true, 0, "test", null));

        public void UnloadModel() { }

        public Task<TranscriptionResult> TranscribeAsync(
            ReadOnlyMemory<float> pcmSamples,
            Action<TranscriptionSegment>? segmentSink,
            CancellationToken ct,
            TranscriptionContext? context = null) =>
            Task.FromResult(new TranscriptionResult([], "", 0, 0, false, 0));

        public void Dispose() => Disposed = true;
    }
}
