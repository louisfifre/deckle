using System.Collections.Concurrent;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;
using static Deckle.Transcription.Tests.LiveTranscriptionPipelineTests;

namespace Deckle.Transcription.Tests;

// The engine entry point that transcribes the audio an interrupted take left
// on disk. The fakes are those of the live pipeline tests; the audio below has
// the same shape as the scripted capture's: per utterance, six voiced frames of
// 800 samples then one silent frame.
//
// In the observability collection because the engine writes Whisp events and
// the take-log tests listen to the provider process-wide.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "integration")]
public sealed class RecoverTakeFromAudioTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, $"recover-take-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static float[] Utterances(int count)
    {
        var audio = new List<float>();
        for (int utterance = 0; utterance < count; utterance++)
        {
            audio.AddRange(Enumerable.Repeat(0.1f, 6 * 800));
            audio.AddRange(new float[800]);
        }
        return audio.ToArray();
    }

    [Fact]
    public async Task TheRecoveredTextGoesToTheClipboardAndNothingIsPasted()
    {
        var backend = new SequencedBackend(["bonjour", "Deckle"]);
        var clipboard = new RecordingClipboardWriter();
        var takeAudio = new TakeAudioRecoveryStore(Path.Combine(_root, "take-audio.dat"));
        var textStore = new DictationRecoveryStore(Path.Combine(_root, "dictation.dat"));
        using var engine = new TranscriptionEngine(
            new StreamingHost(), backend, new ScriptedMicrophoneCapture(utteranceCount: 0),
            clipboard, recovery: textStore, takeAudio: takeAudio);
        var statuses = new ConcurrentQueue<string>();
        var finished = new ConcurrentQueue<TranscriptionOutcome>();
        engine.StatusChanged += statuses.Enqueue;
        engine.TranscriptionFinished += finished.Enqueue;

        RecoveredTake result = await engine.RecoverTakeFromAudioAsync(Utterances(2))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(new RecoveredTake(RecoveredTakeStatus.Delivered, "bonjour\n\nDeckle".Length), result);
        Assert.Equal(1, clipboard.WriteCount);
        Assert.Equal("bonjour\n\nDeckle", clipboard.Text);
        Assert.Equal(2, backend.CallCount);
        // The dictation prompt primes the first utterance, as in a live take.
        Assert.Equal("test prompt", backend.Contexts[0].PrimingText);

        // The HUD sees "transcribing", then the engine back to ready, and one
        // Finished with None: the caller announces the recovery itself.
        Assert.Equal([Loc.Get("Status_Transcribing"), Loc.Get("Status_Ready")], statuses.ToArray());
        Assert.Equal([TranscriptionOutcome.None], finished.ToArray());
        Assert.False(engine.IsBusy);

        // It reads the take audio file and rewrites neither file.
        Assert.False(takeAudio.Exists);
        Assert.False(textStore.Exists);
    }

    [Fact]
    public async Task AColdModelIsPrimedBeforeTheRecoveredAudioIsTranscribed()
    {
        // The first scripted text goes to the prime's dummy call, not to the take.
        var backend = new SequencedBackend(["warm-up", "bonjour", "Deckle"], modelLoaded: false);
        var clipboard = new RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new StreamingHost(), backend, new ScriptedMicrophoneCapture(utteranceCount: 0), clipboard);

        RecoveredTake result = await engine.RecoverTakeFromAudioAsync(Utterances(2))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(RecoveredTakeStatus.Delivered, result.Status);
        Assert.Equal("bonjour\n\nDeckle", clipboard.Text);
        Assert.Equal(3, backend.CallCount);
    }

    [Fact]
    public async Task WhileItRunsAHotkeyAndASecondRecoveryAreRefused()
    {
        var backend = new SequencedBackend(["bonjour", "Deckle"]);
        using var engine = new TranscriptionEngine(
            new StreamingHost(), backend, new ScriptedMicrophoneCapture(utteranceCount: 0),
            new RecordingClipboardWriter());
        ToggleResult? hotkey = null;
        RecoveredTake? second = null;
        engine.StatusChanged += status =>
        {
            // Raised on the recovery's worker, while the engine owns the state.
            if (status != Loc.Get("Status_Transcribing")) return;
            hotkey = engine.RequestToggle(shouldPaste: false);
            second = engine.RecoverTakeFromAudioAsync(Utterances(1)).GetAwaiter().GetResult();
        };

        await engine.RecoverTakeFromAudioAsync(Utterances(2))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(ToggleResult.IgnoredBusy, hotkey);
        Assert.Equal(RecoveredTakeStatus.NotStarted, second?.Status);
    }

    [Fact]
    public async Task ARecoveryIsRefusedWhileADictationIsRecording()
    {
        var backend = new SequencedBackend(["bonjour", "Deckle"]);
        using var engine = new TranscriptionEngine(
            new StreamingHost(), backend, new ScriptedMicrophoneCapture(utteranceCount: 2),
            new RecordingClipboardWriter());
        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
        await backend.AllCallsCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        RecoveredTake result = await engine.RecoverTakeFromAudioAsync(Utterances(1));

        Assert.Equal(RecoveredTakeStatus.NotStarted, result.Status);
        Assert.True(engine.IsRecording);
    }

    [Fact]
    public async Task AudioWithoutSpeechReportsNoTextAndLeavesTheClipboardAlone()
    {
        var clipboard = new RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new StreamingHost(), new SequencedBackend([]), new ScriptedMicrophoneCapture(utteranceCount: 0),
            clipboard);

        RecoveredTake result = await engine.RecoverTakeFromAudioAsync(new float[16_000])
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(RecoveredTakeStatus.NoText, result.Status);
        Assert.Equal(0, clipboard.WriteCount);
        Assert.False(engine.IsBusy);
    }

    [Fact]
    public async Task AModelThatCannotLoadFailsTheRecoveryWithoutTouchingTheClipboard()
    {
        var clipboard = new RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend([], modelLoaded: false, loadFails: true),
            new ScriptedMicrophoneCapture(utteranceCount: 0),
            clipboard);
        try
        {
            RecoveredTake result = await engine.RecoverTakeFromAudioAsync(Utterances(2))
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal(RecoveredTakeStatus.Failed, result.Status);
            Assert.Equal(0, clipboard.WriteCount);
            Assert.False(engine.IsBusy);
        }
        finally
        {
            // The failed load opened a process-wide incident.
            UserIncidentRegistry.Instance.Close(UserIncidents.WhisperModel);
        }
    }

    [Fact]
    public async Task AClipboardThatRefusesTheTextFailsTheRecovery()
    {
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend(["bonjour", "Deckle"]),
            new ScriptedMicrophoneCapture(utteranceCount: 0),
            new RefusingClipboardWriter());

        RecoveredTake result = await engine.RecoverTakeFromAudioAsync(Utterances(2))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(RecoveredTakeStatus.Failed, result.Status);
        Assert.False(engine.IsBusy);
    }

    private sealed class RefusingClipboardWriter : IClipboardWriter
    {
        public ClipboardWriteResult TryWriteText(string text) =>
            new(ClipboardWriteStatus.OpenFailed, 0, 0, 0, Handle: 0);
    }
}
