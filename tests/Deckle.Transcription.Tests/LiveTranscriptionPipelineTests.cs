using System.Collections.Concurrent;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;
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
            engine.RequestToggle(null, shouldPaste: false, requireProfile: false));
        await backend.TwoCallsCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), testCancellation);

        Assert.True(engine.IsRecording);
        Assert.Equal(
            ToggleResult.Stopped,
            engine.RequestToggle(null, shouldPaste: false, requireProfile: false));

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

        public ScriptedMicrophoneCapture(int utteranceCount) =>
            _utteranceCount = utteranceCount;

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
                CaptureOutcome.Completed,
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
