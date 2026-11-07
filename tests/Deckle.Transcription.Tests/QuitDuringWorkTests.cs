using Deckle.Core;
using Deckle.TestSupport;
using Xunit;
using static Deckle.Transcription.Tests.LiveTranscriptionPipelineTests;

namespace Deckle.Transcription.Tests;

// What the engine does when the host quits while it is working
// (TranscriptionEngine.Shutdown.cs). The fakes are the ones of the live
// pipeline tests; the recovery store is a real file in a temporary directory.
//
// In the observability collection because the engine writes Whisp events and
// the take-log tests listen to the provider process-wide.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "integration")]
public sealed class QuitDuringWorkTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, $"quit-during-work-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AQuitDuringRecordingStopsTheTakeAndDeliversItsText()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        var backend = new SequencedBackend([TestInference, "bonjour", "Deckle"]);
        var clipboard = new RecordingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 2),
            clipboard,
            captureTail: TestCaptureTail);
        var outcomes = new List<TranscriptionOutcome>();
        engine.TranscriptionFinished += outcome => { lock (outcomes) outcomes.Add(outcome); };

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
        await backend.AllCallsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);
        Assert.True(engine.IsRecording);

        DrainResult result = await Task.Run(
            () => engine.FinishInFlightWork("test", TimeSpan.FromSeconds(10)), testCancellation);

        Assert.Equal(DrainResult.Finished, result);
        Assert.False(engine.IsBusy);
        Assert.Equal(1, clipboard.WriteCount);
        Assert.Equal("bonjour\n\nDeckle", clipboard.Text);
        lock (outcomes) Assert.Equal([TranscriptionOutcome.ClipboardOnly], outcomes);
    }

    [Fact]
    public async Task AQuitWithNothingInFlightReturnsAtOnce()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend([TestInference]),
            new ScriptedMicrophoneCapture(utteranceCount: 0),
            new RecordingClipboardWriter(),
            captureTail: TestCaptureTail);

        DrainResult result = await Task.Run(
            () => engine.FinishInFlightWork("test", TimeSpan.FromSeconds(10)), testCancellation);

        Assert.Equal(DrainResult.NothingInFlight, result);
    }

    // The raw copy is the moment the recovery file is allowed to go
    // (TranscriptionEngine.Recovery.cs). A clipboard that never answers holds
    // the take before that moment, so the bound passes with the text on disk only.
    [Fact]
    public async Task AQuitPastTheBoundLeavesTheTextInTheRecoveryFile()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        var recovery = new DictationRecoveryStore(Path.Combine(_root, "dictation.recovery"));
        var backend = new SequencedBackend([TestInference, "bonjour", "Deckle"]);
        using var clipboard = new BlockingClipboardWriter();
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 2),
            clipboard,
            recovery: recovery,
            captureTail: TestCaptureTail);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle(shouldPaste: false));
        await backend.AllCallsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), testCancellation);

        try
        {
            DrainResult result = await Task.Run(
                () => engine.FinishInFlightWork("test", TimeSpan.FromMilliseconds(400)),
                testCancellation);

            Assert.Equal(DrainResult.TimedOut, result);
            Assert.Equal(DictationRecoveryReadStatus.Restored, recovery.TryRead(out string kept));
            Assert.Equal("bonjour\n\nDeckle", kept);
        }
        finally
        {
            // Lets the stuck worker finish, so that the Dispose of `using` joins it.
            clipboard.Release();
        }
    }

    [Fact]
    public async Task AHotkeyPressAfterTheQuitBeganStartsNoTake()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        using var engine = new TranscriptionEngine(
            new StreamingHost(),
            new SequencedBackend([TestInference]),
            new ScriptedMicrophoneCapture(utteranceCount: 0),
            new RecordingClipboardWriter(),
            captureTail: TestCaptureTail);

        await Task.Run(
            () => engine.FinishInFlightWork("test", TimeSpan.FromSeconds(1)), testCancellation);

        Assert.Equal(ToggleResult.IgnoredDisposed, engine.RequestToggle(shouldPaste: false));
        Assert.False(engine.IsBusy);
    }

    // Holds the raw copy until the test releases it.
    private sealed class BlockingClipboardWriter : IClipboardWriter, IDisposable
    {
        private readonly ManualResetEventSlim _gate = new(false);

        public void Release() => _gate.Set();

        public ClipboardWriteResult TryWriteText(string text)
        {
            _gate.Wait(TimeSpan.FromSeconds(20));
            return new ClipboardWriteResult(
                ClipboardWriteStatus.Success, text.Length, text.Length, (text.Length + 1) * 2, Handle: 1);
        }

        public void Dispose() => _gate.Dispose();
    }
}
