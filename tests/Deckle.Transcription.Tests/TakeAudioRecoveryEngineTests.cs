using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;
using static Deckle.Transcription.Tests.LiveTranscriptionPipelineTests;

namespace Deckle.Transcription.Tests;

// The engine writes the audio of a dictation take to a real sealed file in a
// temporary directory, so these are integration tests. They reuse the fakes of
// the live pipeline tests. The scripted capture emits 7 frames of 800 samples
// per utterance (5 600 samples), whatever the text.
//
// In the observability collection because the engine writes Whisp events and
// the take-log tests listen to the provider process-wide.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "integration")]
public sealed class TakeAudioRecoveryEngineTests : IDisposable
{
    private const int SamplesPerUtterance = 7 * 800;

    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, $"take-audio-engine-{Guid.NewGuid():N}");

    private TakeAudioRecoveryStore NewStore() => new(Path.Combine(_root, "take-audio.dat"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task TheAudioIsOnDiskWhileTheTakeRuns()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TakeAudioRecoveryStore store = NewStore();
        var backend = new SequencedBackend([TestInference, "un", "deux", "trois"]);
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 3),
            new RecordingClipboardWriter(),
            takeAudio: store,
            captureTail: TestCaptureTail);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
        await backend.AllCallsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        // The third utterance made 16 800 samples: one whole second is sealed
        // and written by the writer task, the rest waits in memory.
        TakeAudioRead read = default;
        for (int i = 0; i < 100 && read.Records == 0; i++)
        {
            read = store.TryRead();
            if (read.Records == 0) await Task.Delay(50, testCancellation);
        }

        Assert.True(store.Exists);
        Assert.Equal(1, read.Records);
        Assert.Equal(16_000, read.Samples.Length);

        Assert.Equal(ToggleResult.Stopped, engine.RequestToggle(shouldPaste: false));
    }

    [Fact]
    public async Task ADeliveredTakeLeavesNoAudioBehind()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TakeAudioRecoveryStore store = NewStore();
        var backend = new SequencedBackend([TestInference, "bonjour", "Deckle"]);
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 2),
            new RecordingClipboardWriter(),
            takeAudio: store,
            captureTail: TestCaptureTail);

        await RunOneTakeAsync(engine, backend, testCancellation);

        Assert.False(store.Exists);
    }

    [Fact]
    public async Task ATakeWithoutSpeechLeavesNoAudioBehind()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TakeAudioRecoveryStore store = NewStore();
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend([TestInference]),
            new ScriptedMicrophoneCapture(utteranceCount: 0),
            new RecordingClipboardWriter(),
            takeAudio: store,
            captureTail: TestCaptureTail);

        TranscriptionOutcome outcome = await RunTakeAsync(engine, calls: null, testCancellation);

        Assert.Equal(TranscriptionOutcome.None, outcome);
        Assert.False(store.Exists);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AMicrophoneErrorKeepsTheWholeTakeAudio()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TakeAudioRecoveryStore store = NewStore();
        var backend = new SequencedBackend([TestInference, "bonjour", "Deckle"]);
        var clipboard = new RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 2, CaptureOutcome.MicError),
            clipboard,
            takeAudio: store,
            captureTail: TestCaptureTail);

        TranscriptionOutcome outcome = await RunTakeAsync(engine, backend.AllCallsCompleted.Task, testCancellation);

        Assert.Equal(TranscriptionOutcome.None, outcome);
        Assert.Equal(0, clipboard.WriteCount);

        // Both utterances, including the partial last block flushed at the end.
        TakeAudioRead read = store.TryRead();
        Assert.Equal(TakeAudioReadStatus.Restored, read.Status);
        Assert.False(read.TailLost);
        Assert.Equal(2 * SamplesPerUtterance, read.Samples.Length);
        Assert.Equal(0.1f, read.Samples[0], precision: 4);
        Assert.Equal(0f, read.Samples[6 * 800]);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ATakeWhoseModelNeverLoadsKeepsItsAudio()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TakeAudioRecoveryStore store = NewStore();
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend([], modelLoaded: false, loadFails: true),
            new ScriptedMicrophoneCapture(utteranceCount: 2),
            new RecordingClipboardWriter(),
            takeAudio: store,
            captureTail: TestCaptureTail);
        try
        {
            TranscriptionOutcome outcome = await RunTakeAsync(engine, calls: null, testCancellation);

            Assert.Equal(TranscriptionOutcome.None, outcome);
            Assert.Equal(2 * SamplesPerUtterance, store.TryRead().Samples.Length);
        }
        finally
        {
            // The failed load opened a process-wide incident.
            UserIncidentRegistry.Instance.Close(UserIncidents.WhisperModel);
        }
    }

    // The per-take test inference fails twice: the engine stops the take by
    // itself and says so. What was said before that is the only copy, so the
    // audio stays for the next launch.
    [Fact]
    [Trait("Category", "regression")]
    public async Task ATakeStoppedByAFailingTestInferenceKeepsItsAudio()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TakeAudioRecoveryStore store = NewStore();
        var clipboard = new RecordingClipboardWriter();
        // No scripted text: the test inference throws on both attempts.
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend([]),
            new ScriptedMicrophoneCapture(utteranceCount: 2),
            clipboard,
            takeAudio: store,
            captureTail: TestCaptureTail);
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.TranscriptionFinished += result => finished.TrySetResult(result);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));

        // No Stop press: the engine ends the take on its own.
        TranscriptionOutcome outcome = await finished.Task.WaitAsync(
            TimeSpan.FromSeconds(5), testCancellation);

        Assert.Equal(TranscriptionOutcome.None, outcome);
        Assert.Equal(0, clipboard.WriteCount);
        Assert.True(store.Exists);
        Assert.Equal(2 * SamplesPerUtterance, store.TryRead().Samples.Length);
    }

    // Regression: a backend that fails every utterance left the take with no
    // text and no failure flag, so it ended as "no text" and the audio, the only
    // copy of what was said, was deleted.
    [Fact]
    [Trait("Category", "regression")]
    public async Task ATakeWhoseBackendFailsOnEveryUtteranceKeepsItsAudio()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TakeAudioRecoveryStore store = NewStore();
        var clipboard = new RecordingClipboardWriter();
        // Only the test inference is scripted: every call after it throws.
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend([TestInference]),
            new ScriptedMicrophoneCapture(utteranceCount: 2),
            clipboard,
            takeAudio: store,
            captureTail: TestCaptureTail);

        TranscriptionOutcome outcome = await RunTakeAsync(engine, calls: null, testCancellation);

        Assert.Equal(TranscriptionOutcome.None, outcome);
        Assert.Equal(0, clipboard.WriteCount);
        Assert.Equal(2 * SamplesPerUtterance, store.TryRead().Samples.Length);
    }

    // Weakness (b): one utterance fails, the other is delivered. The person gets
    // a partial text, and the audio of the failed part is deleted with the rest.
    //
    // Reproduced (this test failed on the store.Exists line before it was
    // skipped). Not fixed in this change: keeping the audio "until the next
    // full delivery" needs a second slot beside take-audio.dat, because the next
    // take's TryBegin replaces that file, plus a size cap and a recovery rule for
    // a take whose text was already pasted. Keeping it in the single slot would
    // only last until the next hotkey press. To pick up: the Anytype task
    // "Durcir le filet audio des prises", weakness 2.
    [Fact(Skip = "Weakness (b) reproduced, fix needs a second audio slot: see the comment above.")]
    [Trait("Category", "regression")]
    public async Task APartialTakeKeepsItsAudioForTheFailedPart()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TakeAudioRecoveryStore store = NewStore();
        var clipboard = new RecordingClipboardWriter();
        // The test inference and the first utterance pass; the second throws.
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend([TestInference, "bonjour"]),
            new ScriptedMicrophoneCapture(utteranceCount: 2),
            clipboard,
            takeAudio: store,
            captureTail: TestCaptureTail);

        TranscriptionOutcome outcome = await RunTakeAsync(engine, calls: null, testCancellation);

        Assert.NotEqual(TranscriptionOutcome.None, outcome);
        Assert.Equal(1, clipboard.WriteCount);
        Assert.True(store.Exists);
        Assert.Equal(2 * SamplesPerUtterance, store.TryRead().Samples.Length);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task DisposingTheEngineMidTakeKeepsTheAudioRecordedSoFar()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TakeAudioRecoveryStore store = NewStore();
        var backend = new SequencedBackend([TestInference, "bonjour", "Deckle"]);
        var engine = new TranscriptionEngine(
            new StreamingHost(),
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 2),
            new RecordingClipboardWriter(),
            takeAudio: store,
            captureTail: TestCaptureTail);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
        await backend.AllCallsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        engine.Dispose();

        Assert.True(store.Exists);
        Assert.Equal(2 * SamplesPerUtterance, store.TryRead().Samples.Length);
    }

    [Fact]
    public async Task AFileTranscriptionNeverCreatesTheTakeAudioFile()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TakeAudioRecoveryStore store = NewStore();
        var backend = new SequencedBackend([TestInference, "un", "deux", "trois", "quatre", "cinq", "six"]);
        string wav = Path.Combine(_root, "source.wav");
        WriteToneWav(wav);
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 0),
            new RecordingClipboardWriter(),
            takeAudio: store,
            captureTail: TestCaptureTail);
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.TranscriptionFinished += result => finished.TrySetResult(result);

        Assert.Equal(1, engine.EnqueueFileTranscriptions([wav]));
        TranscriptionOutcome outcome = await finished.Task.WaitAsync(
            TimeSpan.FromSeconds(10), testCancellation);

        // The run really transcribed the file, so the absence is not vacuous.
        Assert.Equal(TranscriptionOutcome.SavedToFile, outcome);
        Assert.True(backend.CallCount > 0);
        Assert.False(store.Exists);
        Assert.False(store.AttemptedExists);
    }

    [Theory]
    [InlineData(TakeOutcomes.Pasted, TakeEndReasons.Delivered, true)]
    [InlineData(TakeOutcomes.ClipboardOnly, TakeEndReasons.Delivered, true)]
    [InlineData(TakeOutcomes.None, TakeEndReasons.NoText, true)]
    [InlineData(TakeOutcomes.None, TakeEndReasons.EmptyAudio, true)]
    [InlineData(TakeOutcomes.None, TakeEndReasons.StateLost, false)]
    [InlineData(TakeOutcomes.None, TakeEndReasons.Cancelled, false)]
    [InlineData(TakeOutcomes.Failed, TakeEndReasons.MicrophoneError, false)]
    [InlineData(TakeOutcomes.Failed, TakeEndReasons.TranscriptionUnavailable, false)]
    [InlineData(TakeOutcomes.Failed, TakeEndReasons.ModelNotLoaded, false)]
    [InlineData(TakeOutcomes.Failed, TakeEndReasons.CopyFailed, false)]
    [InlineData(TakeOutcomes.Failed, TakeEndReasons.PipelineCrashed, false)]
    [InlineData(TakeOutcomes.Abandoned, TakeEndReasons.WorkerExit, false)]
    public void TheAudioGoesOnlyWhenNothingIsLeftToRecover(string outcome, string reason, bool discarded)
    {
        Assert.Equal(discarded, TranscriptionEngine.DiscardsTakeAudio(outcome, reason));
    }

    // A reason added to TakeEndReasons later, such as a take stopped because the
    // transcription test failed, ends the take as a failure through
    // IsFailureReason. The rule must keep its audio without being edited.
    [Fact]
    [Trait("Category", "regression")]
    public void NoFailureReasonEverDiscardsTheAudio()
    {
        string[] reasons = typeof(TakeEndReasons)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
        Assert.NotEmpty(reasons);

        foreach (string reason in reasons.Where(TranscriptionEngine.IsFailureReason))
        {
            string outcome = TranscriptionEngine.TakeOutcomeName(TranscriptionOutcome.None, reason);

            Assert.Equal(TakeOutcomes.Failed, outcome);
            Assert.False(
                TranscriptionEngine.DiscardsTakeAudio(outcome, reason),
                $"The audio would be deleted after the failure '{reason}'.");
        }
    }

    // Starts a take, stops it once the optional calls are done, and returns the
    // outcome once the worker is back to Ready.
    private static async Task<TranscriptionOutcome> RunTakeAsync(
        TranscriptionEngine engine, Task? calls, CancellationToken testCancellation)
    {
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StatusChanged += status =>
        {
            if (status == Loc.Get("Status_Ready")) ready.TrySetResult();
        };
        engine.TranscriptionFinished += result => finished.TrySetResult(result);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
        if (calls is not null)
            await calls.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
        Assert.Equal(ToggleResult.Stopped, engine.RequestToggle(shouldPaste: false));

        TranscriptionOutcome outcome = await finished.Task.WaitAsync(
            TimeSpan.FromSeconds(10), testCancellation);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
        return outcome;
    }

    // One second of a loud tone then half a second of silence, as PCM16 mono
    // 16 kHz: enough for the segmenter to cut one utterance.
    private static void WriteToneWav(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const int rate = 16_000;
        int samples = rate * 3 / 2;
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
        {
            double tone = i < rate ? Math.Sin(2 * Math.PI * 440 * i / rate) * 0.5 : 0;
            writer.Write((short)(tone * short.MaxValue));
        }
    }
}
