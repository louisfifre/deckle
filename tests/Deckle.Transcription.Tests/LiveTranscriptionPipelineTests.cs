using System.Collections.Concurrent;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "integration")]
public sealed class LiveTranscriptionPipelineTests
{
    [Fact]
    public async Task TwoLiveUtterancesAreConsumedSequentiallyAndDeliveredOnce()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        var capture = new ScriptedMicrophoneCapture(utteranceCount: 2);
        var backend = new SequencedBackend(["bonjour", "Deckle"]);
        var clipboard = new RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            backend,
            capture,
            clipboard);
        var statuses = new ConcurrentQueue<string>();
        var segments = new ConcurrentQueue<string>();
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int finishCount = 0;

        engine.StatusChanged += status =>
        {
            statuses.Enqueue(status);
            if (status == Loc.Get("Status_Ready"))
                ready.TrySetResult();
        };
        engine.NewSegment += segment => segments.Enqueue(segment.Text);
        engine.TranscriptionFinished += outcome =>
        {
            Interlocked.Increment(ref finishCount);
            finished.TrySetResult(outcome);
        };

        Assert.Equal(
            ToggleResult.Started,
            engine.RequestToggle(shouldPaste: false));
        await backend.TwoCallsCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), testCancellation);

        Assert.True(engine.IsRecording);
        Assert.Equal(
            ToggleResult.Stopped,
            engine.RequestToggle(shouldPaste: false));

        Assert.Equal(
            TranscriptionOutcome.ClipboardOnly,
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        Assert.Equal(
            [
                Loc.Get("Status_Recording"),
                Loc.Get("Status_Transcribing"),
                Loc.Get("Status_Ready"),
            ],
            statuses.ToArray());
        Assert.Equal(1, Volatile.Read(ref finishCount));
        Assert.Equal(1, clipboard.WriteCount);
        Assert.Equal("bonjour\n\nDeckle", clipboard.Text);
        Assert.Equal(["bonjour", "Deckle"], segments.ToArray());
        Assert.Equal(2, backend.CallCount);
        Assert.Equal(1, backend.MaxConcurrentCalls);
        Assert.All(backend.SampleCounts, count => Assert.Equal(6 * 800, count));
        Assert.Equal("test prompt", backend.Contexts[0].PrimingText);
        Assert.Equal("test prompt bonjour", backend.Contexts[1].PrimingText);
        Assert.False(engine.IsBusy);
    }

    [Fact]
    public async Task EachTranscribedUtteranceIsPublishedInOrderWithItsText()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        var capture = new ScriptedMicrophoneCapture(utteranceCount: 2);
        var backend = new SequencedBackend(["bonjour", "Deckle"]);
        var clipboard = new RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            backend,
            capture,
            clipboard);
        var utterances = new ConcurrentQueue<TranscribedUtterance>();
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        engine.UtteranceTranscribed += utterances.Enqueue;
        engine.StatusChanged += status =>
        {
            if (status == Loc.Get("Status_Ready"))
                ready.TrySetResult();
        };
        engine.TranscriptionFinished += outcome => finished.TrySetResult(outcome);

        Assert.Equal(
            ToggleResult.Started,
            engine.RequestToggle(shouldPaste: false));
        await backend.TwoCallsCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), testCancellation);
        Assert.Equal(
            ToggleResult.Stopped,
            engine.RequestToggle(shouldPaste: false));

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        TranscribedUtterance[] published = utterances.ToArray();
        Assert.Equal(2, published.Length);
        Assert.Equal(0, published[0].Index);
        Assert.Equal("bonjour", published[0].Text);
        Assert.True(published[0].EndedOnSilence);
        Assert.Equal(1, published[1].Index);
        Assert.Equal("Deckle", published[1].Text);
    }

    [Fact]
    public async Task RecoveryFileHoldsTheRawTextDuringTheTakeAndIsGoneOnceDelivered()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        string root = Path.Combine(AppContext.BaseDirectory, $"live-recovery-{Guid.NewGuid():N}");
        try
        {
            var store = new DictationRecoveryStore(Path.Combine(root, "dictation.dat"));
            var capture = new ScriptedMicrophoneCapture(utteranceCount: 2);
            var backend = new SequencedBackend(["bonjour", "Deckle"]);
            using var engine = new TranscriptionEngine(
                new StreamingHost(),
                backend,
                capture,
                new RecordingClipboardWriter(),
                recovery: store);
            var finished = new TaskCompletionSource<TranscriptionOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.StatusChanged += status =>
            {
                if (status == Loc.Get("Status_Ready")) ready.TrySetResult();
            };
            engine.TranscriptionFinished += outcome => finished.TrySetResult(outcome);

            Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
            await backend.TwoCallsCompleted.Task.WaitAsync(
                TimeSpan.FromSeconds(5), testCancellation);

            // The write follows the backend call on the consumer task, so poll.
            string text = string.Empty;
            for (int i = 0; i < 100 && text != "bonjour\n\nDeckle"; i++)
            {
                store.TryRead(out text);
                if (text != "bonjour\n\nDeckle")
                    await Task.Delay(50, testCancellation);
            }
            Assert.Equal("bonjour\n\nDeckle", text);

            Assert.Equal(ToggleResult.Stopped, engine.RequestToggle(shouldPaste: false));
            // Finished is raised inside finalize, before the file is cleared;
            // Ready comes after the worker has settled.
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

            Assert.False(store.Exists);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // The microphone dropped after two utterances were transcribed. Nothing
    // reached the clipboard, so the file is the only copy of what was said.
    [Fact]
    [Trait("Category", "regression")]
    public async Task RecoveryFileSurvivesAMicrophoneErrorAfterTranscribedUtterances()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        string root = Path.Combine(AppContext.BaseDirectory, $"live-recovery-{Guid.NewGuid():N}");
        try
        {
            var store = new DictationRecoveryStore(Path.Combine(root, "dictation.dat"));
            var clipboard = new RecordingClipboardWriter();
            using var engine = new TranscriptionEngine(
                new StreamingHost(),
                new SequencedBackend(["bonjour", "Deckle"]),
                new ScriptedMicrophoneCapture(utteranceCount: 2, CaptureOutcome.MicError),
                clipboard,
                recovery: store);

            await RunTakeToIdleAsync(engine, store, "bonjour\n\nDeckle", shouldPaste: false, testCancellation);

            Assert.Equal(0, clipboard.WriteCount);
            Assert.Equal(DictationRecoveryReadStatus.Restored, store.TryRead(out string kept));
            Assert.Equal("bonjour\n\nDeckle", kept);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // The raw copy landed, then the paste step failed. A file kept here would
    // overwrite the clipboard with this old text at the next launch.
    [Fact]
    [Trait("Category", "regression")]
    public async Task RecoveryFileIsGoneWhenDeliveryFailsAfterTheRawCopy()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        string root = Path.Combine(AppContext.BaseDirectory, $"live-recovery-{Guid.NewGuid():N}");
        try
        {
            var store = new DictationRecoveryStore(Path.Combine(root, "dictation.dat"));
            var clipboard = new RecordingClipboardWriter();
            using var engine = new TranscriptionEngine(
                new StreamingHost(),
                new SequencedBackend(["bonjour", "Deckle"]),
                new ScriptedMicrophoneCapture(utteranceCount: 2),
                clipboard,
                recovery: store);
            engine.OnReadyToPaste = () => throw new InvalidOperationException("paste failed");

            await RunTakeToIdleAsync(engine, store, "bonjour\n\nDeckle", shouldPaste: true, testCancellation);

            Assert.Equal("bonjour\n\nDeckle", clipboard.Text);
            Assert.False(store.Exists);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // Starts a take, waits until the recovery file holds the expected text,
    // stops it, and returns once the worker has settled back to idle.
    private static async Task RunTakeToIdleAsync(
        TranscriptionEngine engine,
        DictationRecoveryStore store,
        string expectedText,
        bool shouldPaste,
        CancellationToken testCancellation)
    {
        int stopped = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StatusChanged += status =>
        {
            // Only the Ready the worker raises once it has settled counts.
            if (Volatile.Read(ref stopped) == 1 && status == Loc.Get("Status_Ready"))
                ready.TrySetResult();
        };

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste));

        // The write follows the backend call on the consumer task, so poll.
        string text = string.Empty;
        for (int i = 0; i < 100 && text != expectedText; i++)
        {
            store.TryRead(out text);
            if (text != expectedText)
                await Task.Delay(50, testCancellation);
        }
        Assert.Equal(expectedText, text);

        Volatile.Write(ref stopped, 1);
        Assert.Equal(ToggleResult.Stopped, engine.RequestToggle(shouldPaste));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
    }
    // Each utterance is longer than the minimum threshold and ends on a full
    // stop, so each one closes a batch on its own, with no carried fragment.
    private static readonly string FirstLongUtterance = string.Concat(
        Enumerable.Repeat("Je commence par le premier sujet de cette dictée. ", 5)).Trim();
    private static readonly string SecondLongUtterance = string.Concat(
        Enumerable.Repeat("Puis je passe au second sujet, qui est différent. ", 5)).Trim();

    [Fact]
    public async Task BatchRewritingRewritesEachBatchAndDeliversTheirJoinedRewrites()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        var capture = new ScriptedMicrophoneCapture(utteranceCount: 2);
        var backend = new SequencedBackend([FirstLongUtterance, SecondLongUtterance]);
        var clipboard = new RecordingClipboardWriter();
        var rewrite = new BracketingRewriteService();
        using var engine = new TranscriptionEngine(
            RewritingHost(batchRewriting: true),
            backend,
            capture,
            clipboard,
            rewrite);

        await RunOneTakeAsync(engine, backend, testCancellation);

        // The batches run in parallel on the thread pool, so the calls are
        // compared without their order; the assembly order is checked on the
        // clipboard text below.
        Assert.Equal(
            new[] { FirstLongUtterance, SecondLongUtterance }.Order(),
            rewrite.Inputs.Order());
        // Raw copy, then the assembled rewrite: one copy per take, never one
        // per batch. The first utterance ended on silence, so its batch ends on
        // a pause and the seam is a paragraph break.
        Assert.Equal(2, clipboard.WriteCount);
        Assert.Equal(
            "[" + FirstLongUtterance + "]\n\n[" + SecondLongUtterance + "]",
            clipboard.Text);
    }

    [Fact]
    public async Task WithBatchRewritingOffTheWholeTakeIsRewrittenOnce()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        var capture = new ScriptedMicrophoneCapture(utteranceCount: 2);
        var backend = new SequencedBackend([FirstLongUtterance, SecondLongUtterance]);
        var clipboard = new RecordingClipboardWriter();
        var rewrite = new BracketingRewriteService();
        using var engine = new TranscriptionEngine(
            RewritingHost(batchRewriting: false),
            backend,
            capture,
            clipboard,
            rewrite);

        await RunOneTakeAsync(engine, backend, testCancellation);

        string rawTake = FirstLongUtterance + "\n\n" + SecondLongUtterance;
        Assert.Equal([rawTake], rewrite.Inputs);
        Assert.Equal(2, clipboard.WriteCount);
        Assert.Equal("[" + rawTake + "]", clipboard.Text);
    }

    // Starts a take, stops it once the backend has transcribed both scripted
    // utterances, and waits until the engine is back to Ready.
    private static async Task RunOneTakeAsync(
        TranscriptionEngine engine, SequencedBackend backend, CancellationToken testCancellation)
    {
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StatusChanged += status =>
        {
            if (status == Loc.Get("Status_Ready"))
                ready.TrySetResult();
        };
        engine.TranscriptionFinished += outcome => finished.TrySetResult(outcome);

        Assert.Equal(
            ToggleResult.Started,
            engine.RequestToggle(shouldPaste: false));
        await backend.TwoCallsCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), testCancellation);
        Assert.Equal(
            ToggleResult.Stopped,
            engine.RequestToggle(shouldPaste: false));

        Assert.Equal(
            TranscriptionOutcome.ClipboardOnly,
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
    }

    // Rewriting on, with the first default profile active and the batch
    // threshold at its minimum.
    private static StreamingHost RewritingHost(bool batchRewriting)
    {
        var host = new StreamingHost();
        LlmSettings llm = host.Llm;
        llm.Enabled = true;
        RewriteProfile profile = llm.Profiles[0];
        profile.Id = "test-profile";
        llm.ActiveProfileId = profile.Id;
        llm.ActiveProfileName = profile.Name;
        llm.BatchRewriting.Enabled = batchRewriting;
        llm.BatchRewriting.ThresholdChars = BatchRewriteSettings.MinThresholdChars;
        return host;
    }

    // Records every Rewrite input and returns it in brackets, so the delivered
    // text shows which pieces were rewritten and how they were joined.
    private sealed class BracketingRewriteService : IRewriteService
    {
        private readonly ConcurrentQueue<string> _inputs = new();

        public string[] Inputs => _inputs.ToArray();

        public GenerationResult Rewrite(string text, RewriteProfile profile)
        {
            _inputs.Enqueue(text);
            return new GenerationResult("[" + text + "]", 1, 0, 0, 0, 0, 0);
        }

        public GenerationResult RewriteParagraph(string paragraph, CancellationToken cancellationToken) => default;

        public GenerationResult RewriteSentence(string sentence, CancellationToken cancellationToken) => default;
    }

    private sealed class StreamingHost : ITranscriptionEngineHost
    {
        public StreamingHost()
        {
            Transcription.Streaming.Strategy = PipelineStrategyKind.Streaming;
            Transcription.Streaming.SpeechTrim.Enabled = false;
            Transcription.Streaming.Segmenter.HangoverMaxMs = 50;
            Transcription.Streaming.Segmenter.HangoverMinMs = 50;
            Transcription.Streaming.Segmenter.HangoverRampStartMs = 50;
            Transcription.Streaming.Segmenter.HangoverRampEndMs = 50;
            Transcription.Streaming.Segmenter.MarginMs = 0;
            Transcription.Streaming.Segmenter.MinUtteranceMs = 250;
            Transcription.Engine.InitialPrompt = "test prompt";
            Llm.Enabled = false;
        }

        public TranscriptionSettings Transcription { get; } = new();
        public CaptureSettings Audio { get; } = new();
        public TelemetrySettings Telemetry { get; } = new();
        public LlmSettings Llm { get; } = new();
        public string ResolveModelsDirectory() => Path.GetTempPath();
        public void SaveSettings() { }
        public void ApplyLevelWindow(LevelWindowSettings lw) { }
    }

    private sealed class ScriptedMicrophoneCapture : IMicrophoneCapture
    {
        private const int FrameSamples = 800;
        private readonly int _utteranceCount;
        private readonly CaptureOutcome _outcome;

        public ScriptedMicrophoneCapture(
            int utteranceCount,
            CaptureOutcome outcome = CaptureOutcome.Completed)
        {
            _utteranceCount = utteranceCount;
            _outcome = outcome;
        }

        public event Action<float>? AudioLevel { add { } remove { } }
        public event Action<CaptureFrame>? Frame;
        public event Action? CaptureStarted;
        public event Action? LowAudioDetected { add { } remove { } }

        public ProbeResult Probe(int deviceId) =>
            new(true, MicErrorKind.None, 0);

        public CaptureResult Record(
            IAudioRecordingHost host,
            CancellationToken cancellationToken)
        {
            CaptureStarted?.Invoke();
            var captured = new List<float>();
            for (int utterance = 0; utterance < _utteranceCount; utterance++)
            {
                for (int frameIndex = 0; frameIndex < 6; frameIndex++)
                {
                    var samples = new float[FrameSamples];
                    Array.Fill(samples, 0.1f);
                    captured.AddRange(samples);
                    Frame?.Invoke(new CaptureFrame(samples, 0.1f));
                }

                var silence = new float[FrameSamples];
                captured.AddRange(silence);
                Frame?.Invoke(new CaptureFrame(silence, 0f));
            }

            if (!cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The scripted live capture was not stopped.");

            return new CaptureResult(
                captured.ToArray(),
                Telemetry: null,
                _outcome,
                TimeSpan.Zero,
                MmsysErr: 0);
        }

        public void Dispose() { }
    }

    private sealed class SequencedBackend : IAsrBackend
    {
        private readonly IReadOnlyList<string> _texts;
        private readonly object _gate = new();
        private readonly List<int> _sampleCounts = new();
        private readonly List<TranscriptionContext> _contexts = new();
        private int _activeCalls;
        private int _callCount;
        private int _maxConcurrentCalls;

        public SequencedBackend(IReadOnlyList<string> texts) => _texts = texts;

        public string Name => "test";
        public bool IsModelLoaded => true;
        public string? DetectedAccelerator => "test";
        public int CallCount => Volatile.Read(ref _callCount);
        public int MaxConcurrentCalls => Volatile.Read(ref _maxConcurrentCalls);
        public IReadOnlyList<int> SampleCounts { get { lock (_gate) return _sampleCounts.ToArray(); } }
        public IReadOnlyList<TranscriptionContext> Contexts { get { lock (_gate) return _contexts.ToArray(); } }
        public TaskCompletionSource TwoCallsCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ModelLoadResult> LoadModelAsync(CancellationToken ct) =>
            Task.FromResult(new ModelLoadResult(true, 0, "test", null));

        public void UnloadModel() { }

        public async Task<TranscriptionResult> TranscribeAsync(
            ReadOnlyMemory<float> pcmSamples,
            Action<TranscriptionSegment>? segmentSink,
            CancellationToken ct,
            TranscriptionContext? context = null)
        {
            int active = Interlocked.Increment(ref _activeCalls);
            lock (_gate)
                _maxConcurrentCalls = Math.Max(_maxConcurrentCalls, active);
            try
            {
                int callIndex = Interlocked.Increment(ref _callCount) - 1;
                string text = _texts[callIndex];
                lock (_gate)
                {
                    _sampleCounts.Add(pcmSamples.Length);
                    _contexts.Add(context!);
                }

                await Task.Yield();
                var segment = new TranscriptionSegment(text, 0, 10, 1f, 0f);
                segmentSink?.Invoke(segment);
                if (callIndex == 1)
                    TwoCallsCompleted.TrySetResult();
                return new TranscriptionResult([segment], text, 1, 0, false, 0);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public void Dispose() { }
    }

    private sealed class RecordingClipboardWriter : IClipboardWriter
    {
        private int _writeCount;

        public int WriteCount => Volatile.Read(ref _writeCount);
        public string? Text { get; private set; }

        public ClipboardWriteResult TryWriteText(string text)
        {
            Text = text;
            Interlocked.Increment(ref _writeCount);
            return new ClipboardWriteResult(
                ClipboardWriteStatus.Success,
                text.Length,
                text.Length,
                (text.Length + 1) * 2,
                Handle: 1);
        }
    }

}
