using System.Diagnostics.Tracing;
using Deckle.Audio;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm.Rewrite;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Tests;

// What the journal says about the model: how each load and each warmup ended and
// why, and that a model which stays unavailable is one incident, not one line per
// take. A cold file batch is the seam: the worker primes the backend before it
// reads a file, so a missing file keeps the test free of audio.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class WhispModelLifecycleTests
{
    private const string Provider = "Deckle-Whisp";

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    private static bool TravelsInTheJournal(EventWrittenEventArgs e)
        => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Journal) != 0;

    private static string MissingPath() =>
        Path.Combine(Path.GetTempPath(), $"deckle-model-{Guid.NewGuid():N}.wav");

    // Runs one cold batch to its end and waits for the warmup to say how it ended.
    private static async Task RunColdBatchAsync(
        TranscriptionEngine engine, TestEventListener listener)
    {
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished(TranscriptionOutcome outcome) => finished.TrySetResult(outcome);
        engine.TranscriptionFinished += OnFinished;
        try
        {
            engine.EnqueueFileTranscriptions([MissingPath()]);
            await finished.Task.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            engine.TranscriptionFinished -= OnFinished;
        }

        // The prime runs beside the batch; its end is written when it returns.
        int warmupEnds = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        int expected = listener.Events.Count(e => e.EventId == DeckleWhispSource.EvtWarmupStart);
        while (DateTime.UtcNow < deadline)
        {
            warmupEnds = listener.Events.Count(IsWarmupEnd);
            if (warmupEnds >= expected && !engine.IsBusy) return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Fail("The warmup did not end.");
    }

    private static bool IsWarmupEnd(EventWrittenEventArgs e)
        => e.EventId is DeckleWhispSource.EvtWarmupComplete
            or DeckleWhispSource.EvtWarmupCancelled
            or DeckleWhispSource.EvtWarmupFailed;

    [Fact]
    public async Task AColdLoadAndItsWarmupEachPairTheirStartAndEndInTheJournal()
    {
        using var listener = new TestEventListener(Provider);
        using var engine = new TranscriptionEngine(new FakeHost(), new ScriptedBackend(loadFails: false));

        await RunColdBatchAsync(engine, listener);

        EventWrittenEventArgs[] journal = listener.Events.Where(TravelsInTheJournal).ToArray();
        EventWrittenEventArgs loading = Assert.Single(
            journal, e => e.EventId == DeckleWhispSource.EvtModelLoading);
        EventWrittenEventArgs loaded = Assert.Single(
            journal, e => e.EventId == DeckleWhispSource.EvtModelLoaded);
        Assert.Equal(Field(loading, WorkId.FieldName), Field(loaded, WorkId.FieldName));
        Assert.Equal("35", Field(loaded, "load_ms"));
        Assert.Equal("scripted", Field(loaded, "device"));

        EventWrittenEventArgs warmupStart = Assert.Single(
            journal, e => e.EventId == DeckleWhispSource.EvtWarmupStart);
        EventWrittenEventArgs warmupEnd = Assert.Single(
            journal, e => e.EventId == DeckleWhispSource.EvtWarmupComplete);
        Assert.Equal(Field(warmupStart, WorkId.FieldName), Field(warmupEnd, WorkId.FieldName));
        Assert.NotEqual(Field(loading, WorkId.FieldName), Field(warmupStart, WorkId.FieldName));
        Assert.True(long.Parse(Field(warmupEnd, "elapsed_ms")) >= 0);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AModelThatStaysUnavailableIsOneIncidentAndItsRecoveryRepeatsTheIncidentId()
    {
        using var listener = new TestEventListener(Provider);
        var backend = new ScriptedBackend(loadFails: true);
        using var engine = new TranscriptionEngine(new FakeHost(), backend);

        await RunColdBatchAsync(engine, listener);
        await RunColdBatchAsync(engine, listener);
        backend.LoadFails = false;
        await RunColdBatchAsync(engine, listener);

        EventWrittenEventArgs[] journal = listener.Events.Where(TravelsInTheJournal).ToArray();

        // The first failure: a start and an Error that share an id.
        EventWrittenEventArgs loading = Assert.Single(
            journal, e => e.EventId == DeckleWhispSource.EvtModelLoading);
        EventWrittenEventArgs unavailable = Assert.Single(
            journal, e => e.EventId == DeckleWhispSource.EvtModelUnavailable);
        Assert.Equal(EventLevel.Error, unavailable.Level);
        Assert.Equal(Field(loading, WorkId.FieldName), Field(unavailable, WorkId.FieldName));
        Assert.Equal("file_not_found", Field(unavailable, "reason"));

        // The later attempts are told in the detail, never in the journal.
        Assert.Equal(
            2,
            listener.Events.Count(e => e.EventId == DeckleWhispSource.EvtModelLoadRetrying));
        Assert.DoesNotContain(journal, e => e.EventId == DeckleWhispSource.EvtModelLoadRetrying);
        Assert.DoesNotContain(journal, e => e.EventId == DeckleWhispSource.EvtModelLoadRetryFailed);

        // The recovery closes the incident that the Error opened.
        EventWrittenEventArgs recovered = Assert.Single(
            journal, e => e.EventId == DeckleWhispSource.EvtModelRecovered);
        Assert.Equal(Field(unavailable, WorkId.FieldName), Field(recovered, WorkId.FieldName));
        Assert.Equal("scripted", Field(recovered, "device"));
        Assert.DoesNotContain(journal, e => e.EventId == DeckleWhispSource.EvtModelLoaded);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AWarmupThatCannotLoadTheModelEndsInAWarningWithTheStartsId()
    {
        using var listener = new TestEventListener(Provider);
        using var engine = new TranscriptionEngine(
            new FakeHost(), new ScriptedBackend(loadFails: true));

        await RunColdBatchAsync(engine, listener);

        EventWrittenEventArgs warmupStart = Assert.Single(
            listener.Events, e => e.EventId == DeckleWhispSource.EvtWarmupStart);
        Assert.True(TravelsInTheJournal(warmupStart));
        EventWrittenEventArgs failed = Assert.Single(
            listener.Events, e => e.EventId == DeckleWhispSource.EvtWarmupFailed);
        Assert.Equal(EventLevel.Warning, failed.Level);
        Assert.Equal(Field(warmupStart, WorkId.FieldName), Field(failed, WorkId.FieldName));
        Assert.Equal("model_load_failed", Field(failed, "reason"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AFailedIdleUnloadIsADegradationNotAStop()
    {
        using var listener = new TestEventListener(Provider);

        DeckleWhispSource.Log.ModelIdleUnloadFailed("InvalidOperationException", "x");

        EventWrittenEventArgs failed = Assert.Single(listener.Events);
        Assert.Equal(DeckleWhispSource.EvtModelIdleUnloadFailed, failed.EventId);
        Assert.Equal(EventLevel.Warning, failed.Level);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AFallbackNamesWhatWasAskedAndWhatWasObtained()
    {
        using var listener = new TestEventListener(Provider);

        DeckleWhispSource.Log.ModelFallback("ggml-large-v3-turbo.bin", "ggml-small.bin");

        EventWrittenEventArgs fallback = Assert.Single(listener.Events);
        Assert.Equal(EventLevel.Warning, fallback.Level);
        Assert.Equal("ggml-large-v3-turbo.bin", Field(fallback, "configured"));
        Assert.Equal("ggml-small.bin", Field(fallback, "installed"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AnIgnoredModelPathNamesTheFileUsedAndKeepsTheVariablesValueInTheDetail()
    {
        using var listener = new TestEventListener(Provider);

        DeckleWhispSource.Log.ModelPathEnvIgnored("ggml-small.bin");
        DeckleWhispSource.Log.ModelPathEnvIgnoredDetail(@"C:\Users\Someone\model.bin", "ggml-small.bin");

        EventWrittenEventArgs warning = Assert.Single(
            listener.Events, e => e.EventId == DeckleWhispSource.EvtModelPathEnvIgnored);
        Assert.Equal(EventLevel.Warning, warning.Level);
        Assert.Equal("ggml-small.bin", Field(warning, "fallback_file"));
        Assert.DoesNotContain(warning.PayloadNames!, name => name == "env_path");
        EventWrittenEventArgs detail = Assert.Single(
            listener.Events, e => e.EventId == DeckleWhispSource.EvtModelPathEnvIgnoredDetail);
        Assert.Equal(EventLevel.Verbose, detail.Level);
    }

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

    // A backend that is not loaded until a load succeeds, and whose load can be
    // told to fail the way a missing model file does.
    private sealed class ScriptedBackend(bool loadFails) : IAsrBackend
    {
        private volatile bool _loaded;

        public bool LoadFails { get; set; } = loadFails;
        public string Name => "scripted";
        public bool IsModelLoaded => _loaded;
        public string? DetectedAccelerator => "scripted";

        public Task<ModelLoadResult> LoadModelAsync(CancellationToken ct)
        {
            if (LoadFails)
                return Task.FromResult(new ModelLoadResult(false, 0, null, "file_not_found"));
            _loaded = true;
            return Task.FromResult(new ModelLoadResult(true, 35, "scripted", null));
        }

        public void UnloadModel() => _loaded = false;

        public Task<TranscriptionResult> TranscribeAsync(
            ReadOnlyMemory<float> pcmSamples,
            Action<TranscriptionSegment>? segmentSink,
            CancellationToken ct,
            TranscriptionContext? context = null) =>
            Task.FromResult(new TranscriptionResult([], "", 0, 0, false, 0));

        public void Dispose() { }
    }
}
