using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;
using static Deckle.Transcription.Tests.LiveTranscriptionPipelineTests;

namespace Deckle.Transcription.Tests;

// The launch-time decision about a lost take's audio, over real sealed files in
// a temporary directory and a scripted transcription. The audio is a synthetic
// ramp, never speech.
//
// In the observability collection because some tests listen to the Whisp
// provider process-wide.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "integration")]
public sealed class TakeAudioRecoveryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, $"take-audio-recovery-{Guid.NewGuid():N}");

    private readonly TakeAudioRecoveryStore _audio;
    private readonly DictationRecoveryStore _text;

    public TakeAudioRecoveryTests()
    {
        _audio = new TakeAudioRecoveryStore(Path.Combine(_root, "recovery", "take-audio.dat"));
        _text = new DictationRecoveryStore(Path.Combine(_root, "recovery", "dictation.dat"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static float[] Ramp(int length)
    {
        var samples = new float[length];
        for (int i = 0; i < length; i++)
            samples[i] = (i % 2000 - 1000) / 32768f;
        return samples;
    }

    // A take that died: audio on disk, and the text of its first utterances.
    private float[] LeaveALostTake(bool withText = true)
    {
        float[] samples = Ramp(20_000);
        Assert.True(_audio.TryBegin());
        Assert.True(_audio.TryAppend(samples.AsSpan(0, 16_000)));
        Assert.True(_audio.TryAppend(samples.AsSpan(16_000)));
        _audio.Close();
        if (withText) Assert.True(_text.TryWrite("Sample sentence."));
        return samples;
    }

    private TakeAudioRecovery Recovery(Func<float[], Task<RecoveredTake>> transcribe) =>
        new(_audio, _text, transcribe);

    [Fact]
    public async Task WithoutAnyFileNothingRunsAndTheTranscriptionIsNotCalled()
    {
        bool called = false;

        TakeAudioRecoveryResult result = await Recovery(_ =>
        {
            called = true;
            return Task.FromResult(new RecoveredTake(RecoveredTakeStatus.Delivered, 1));
        }).TryRecoverAsync();

        Assert.False(result.Recovered);
        Assert.False(called);
    }

    [Fact]
    public async Task ADeliveredRecoveryRemovesTheAudioAndTheTextCopy()
    {
        float[] samples = LeaveALostTake();
        float[]? received = null;
        bool setAsideDuringTheRun = false;

        TakeAudioRecoveryResult result = await Recovery(audio =>
        {
            received = audio;
            // The crash protection: while the run goes on, only the marker exists.
            setAsideDuringTheRun = _audio.AttemptedExists && !_audio.Exists;
            return Task.FromResult(new RecoveredTake(RecoveredTakeStatus.Delivered, 42));
        }).TryRecoverAsync();

        Assert.Equal(new TakeAudioRecoveryResult(true, 42), result);
        Assert.Equal(samples, received);
        Assert.True(setAsideDuringTheRun);
        Assert.False(_audio.Exists);
        Assert.False(_audio.AttemptedExists);
        Assert.False(_text.Exists);
    }

    [Theory]
    [InlineData(RecoveredTakeStatus.Failed)]
    [InlineData(RecoveredTakeStatus.NotStarted)]
    public async Task AFailedRunPutsTheAudioBackAndKeepsTheTextCopy(RecoveredTakeStatus status)
    {
        float[] samples = LeaveALostTake();

        TakeAudioRecoveryResult result = await Recovery(
            _ => Task.FromResult(new RecoveredTake(status))).TryRecoverAsync();

        Assert.False(result.Recovered);
        Assert.True(_audio.Exists);
        Assert.False(_audio.AttemptedExists);
        Assert.Equal(samples, _audio.TryRead().Samples);
        Assert.True(_text.Exists);
    }

    [Fact]
    public async Task ARunThatThrowsPutsTheAudioBackAndKeepsTheTextCopy()
    {
        LeaveALostTake();

        TakeAudioRecoveryResult result = await Recovery(
            _ => throw new InvalidOperationException("scripted")).TryRecoverAsync();

        Assert.False(result.Recovered);
        Assert.True(_audio.Exists);
        Assert.False(_audio.AttemptedExists);
        Assert.True(_text.Exists);
    }

    [Fact]
    public async Task AudioWithoutSpeechIsDiscardedAndTheTextCopyIsLeftForTheFallback()
    {
        LeaveALostTake();

        TakeAudioRecoveryResult result = await Recovery(
            _ => Task.FromResult(new RecoveredTake(RecoveredTakeStatus.NoText))).TryRecoverAsync();

        Assert.False(result.Recovered);
        Assert.False(_audio.Exists);
        Assert.False(_audio.AttemptedExists);
        Assert.True(_text.Exists);
    }

    // A previous run set the file aside and never finished: the app probably
    // died on this audio. It is not run again, and nothing is deleted.
    [Fact]
    [Trait("Category", "regression")]
    public async Task APreviousAttemptThatNeverFinishedIsNotRetried()
    {
        LeaveALostTake();
        Assert.True(_audio.MarkAttempted());
        // A newer take has since written its own file.
        Assert.True(_audio.TryBegin());
        Assert.True(_audio.TryAppend(Ramp(800)));
        _audio.Close();
        bool called = false;

        TakeAudioRecoveryResult result = await Recovery(_ =>
        {
            called = true;
            return Task.FromResult(new RecoveredTake(RecoveredTakeStatus.Delivered, 1));
        }).TryRecoverAsync();

        Assert.False(result.Recovered);
        Assert.False(called);
        Assert.True(_audio.AttemptedExists);
        Assert.True(_audio.Exists);
        Assert.True(_text.Exists);
    }

    [Fact]
    public async Task AnUnreadableFileIsDiscardedWithoutRunning()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_audio.FilePath)!);
        File.WriteAllBytes(_audio.FilePath, new byte[40]);
        bool called = false;

        TakeAudioRecoveryResult result = await Recovery(_ =>
        {
            called = true;
            return Task.FromResult(new RecoveredTake(RecoveredTakeStatus.Delivered, 1));
        }).TryRecoverAsync();

        Assert.False(result.Recovered);
        Assert.False(called);
        Assert.False(_audio.Exists);
        Assert.False(_audio.AttemptedExists);
    }

    [Fact]
    public async Task AFileWithoutAnyRecordIsDiscardedWithoutRunning()
    {
        Assert.True(_audio.TryBegin());
        _audio.Close();
        bool called = false;

        TakeAudioRecoveryResult result = await Recovery(_ =>
        {
            called = true;
            return Task.FromResult(new RecoveredTake(RecoveredTakeStatus.Delivered, 1));
        }).TryRecoverAsync();

        Assert.False(result.Recovered);
        Assert.False(called);
        Assert.False(_audio.Exists);
        Assert.False(_audio.AttemptedExists);
    }

    // The whole chain with a real engine: the audio a take left behind comes out
    // as text on the clipboard, and both files are gone.
    [Fact]
    public async Task ARealEngineTurnsTheSavedAudioIntoClipboardText()
    {
        // Same shape as the scripted capture: two utterances, each ended by a
        // silent frame.
        var audio = new List<float>();
        for (int utterance = 0; utterance < 2; utterance++)
        {
            audio.AddRange(Enumerable.Repeat(0.1f, 6 * 800));
            audio.AddRange(new float[800]);
        }
        Assert.True(_audio.TryBegin());
        Assert.True(_audio.TryAppend(audio.ToArray()));
        _audio.Close();
        Assert.True(_text.TryWrite("Only the first words."));
        var clipboard = new RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend([TestInference, "bonjour", "Deckle"]),
            new ScriptedMicrophoneCapture(utteranceCount: 0),
            clipboard);

        TakeAudioRecoveryResult result = await new TakeAudioRecovery(
                _audio, _text, engine.RecoverTakeFromAudioAsync)
            .TryRecoverAsync()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(new TakeAudioRecoveryResult(true, "bonjour\n\nDeckle".Length), result);
        Assert.Equal("bonjour\n\nDeckle", clipboard.Text);
        Assert.Equal(1, clipboard.WriteCount);
        Assert.False(_audio.Exists);
        Assert.False(_audio.AttemptedExists);
        Assert.False(_text.Exists);
    }

    // The launch-time chain with a test inference that fails: not a crash, not
    // a loss. The audio goes back to .dat for the next launch, the text copy is
    // untouched (the app restores it), and nothing reached the clipboard.
    [Fact]
    public async Task ARealEngineWhoseTestInferenceFailsKeepsTheAudioAndTheTextCopy()
    {
        Assert.True(_audio.TryBegin());
        Assert.True(_audio.TryAppend(Enumerable.Repeat(0.1f, 6 * 800).ToArray()));
        _audio.Close();
        Assert.True(_text.TryWrite("Only the first words."));
        var clipboard = new RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend([]),
            new ScriptedMicrophoneCapture(utteranceCount: 0),
            clipboard);

        TakeAudioRecoveryResult result = await new TakeAudioRecovery(
                _audio, _text, engine.RecoverTakeFromAudioAsync)
            .TryRecoverAsync()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(result.Recovered);
        Assert.Equal(0, clipboard.WriteCount);
        Assert.True(_audio.Exists);
        Assert.False(_audio.AttemptedExists);
        Assert.True(_text.Exists);
        Assert.False(engine.IsBusy);
    }

    [Fact]
    [Trait("Category", "observability")]
    public async Task ARecoveryWritesOneStartAndOneEndSharingTheirWorkIdAndNoContent()
    {
        LeaveALostTake();
        using var listener = new TestEventListener("Deckle-Whisp");

        await Recovery(_ => Task.FromResult(new RecoveredTake(RecoveredTakeStatus.Delivered, 42)))
            .TryRecoverAsync();

        EventWrittenEventArgs started = Assert.Single(
            listener.Events, e => e.EventName == nameof(DeckleWhispSource.TakeAudioRecoveryStarted));
        EventWrittenEventArgs recovered = Assert.Single(
            listener.Events, e => e.EventName == nameof(DeckleWhispSource.TakeAudioRecovered));
        Assert.Equal(PayloadOf(started, "work_id"), PayloadOf(recovered, "work_id"));
        Assert.Equal(2, PayloadOf(started, "records"));
        Assert.Equal(42, PayloadOf(recovered, "chars"));

        // Sizes, counts and ids only: no samples, no text.
        Assert.All(listener.Events, e =>
            Assert.DoesNotContain(e.Payload!, value => value is Array));
    }

    [Fact]
    [Trait("Category", "observability")]
    public async Task AFailedRecoveryEndsItsStartWithAWarningSharingTheWorkId()
    {
        LeaveALostTake();
        using var listener = new TestEventListener("Deckle-Whisp");

        await Recovery(_ => Task.FromResult(new RecoveredTake(RecoveredTakeStatus.Failed)))
            .TryRecoverAsync();

        EventWrittenEventArgs started = Assert.Single(
            listener.Events, e => e.EventName == nameof(DeckleWhispSource.TakeAudioRecoveryStarted));
        EventWrittenEventArgs failed = Assert.Single(
            listener.Events, e => e.EventName == nameof(DeckleWhispSource.TakeAudioRecoveryFailed));
        Assert.Equal(PayloadOf(started, "work_id"), PayloadOf(failed, "work_id"));
        Assert.Equal("transcription_failed", PayloadOf(failed, "reason"));
        Assert.Equal(true, PayloadOf(failed, "kept"));
        Assert.Equal(EventLevel.Warning, failed.Level);
    }

    [Fact]
    [Trait("Category", "observability")]
    public async Task ASkippedRecoveryWarnsWithItsReason()
    {
        LeaveALostTake();
        Assert.True(_audio.MarkAttempted());
        using var listener = new TestEventListener("Deckle-Whisp");

        await Recovery(_ => Task.FromResult(new RecoveredTake(RecoveredTakeStatus.Delivered, 1)))
            .TryRecoverAsync();

        EventWrittenEventArgs skipped = Assert.Single(
            listener.Events, e => e.EventName == nameof(DeckleWhispSource.TakeAudioRecoverySkipped));
        Assert.Equal("previous_attempt_crashed", PayloadOf(skipped, "reason"));
        Assert.Equal(true, PayloadOf(skipped, "kept"));
        Assert.Equal(EventLevel.Warning, skipped.Level);
    }

    [Fact]
    [Trait("Category", "observability")]
    public void TheTakeAudioEventsThatOpenAnIncidentHaveTheirCloserInTheLog()
    {
        string[] closers =
        [
            nameof(DeckleWhispSource.TakeAudioWriteRecovered),
            nameof(DeckleWhispSource.TakeAudioRecovered),
        ];

        string[] outsideTheLog = ProviderManifest.EventsOf(typeof(DeckleWhispSource))
            .Where(declared => closers.Contains(declared.Name)
                && !Deckle.Diagnostics.ObservationTags.IsMilestone(declared.Tags))
            .Select(declared => declared.Name)
            .ToArray();

        Assert.Empty(outsideTheLog);
    }

    private static object? PayloadOf(EventWrittenEventArgs e, string name) =>
        e.Payload![e.PayloadNames!.IndexOf(name)];
}
