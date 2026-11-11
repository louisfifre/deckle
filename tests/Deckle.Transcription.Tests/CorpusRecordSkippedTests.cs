using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;
using static Deckle.Transcription.Tests.LiveTranscriptionPipelineTests;
using Deckle.Localization;

namespace Deckle.Transcription.Tests;

// RecordCorpus used to return without a trace when it had nothing to write, so
// a corpus that stayed empty could not be explained from the log. Each gate now
// says its reason through CorpusRecordSkipped, which carries a reason code and
// never text. These tests run a real engine on a scripted capture and listen to
// the Whisp provider as the fake sink.
//
// In the observability collection because the listener is process-wide.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class CorpusRecordSkippedTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, $"corpus-skipped-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string[] SkipReasons(TestEventListener listener) =>
        listener.Events
            .Where(e => e.EventName == nameof(DeckleWhispSource.CorpusRecordSkipped))
            .Select(e => (string)e.Payload![e.PayloadNames!.IndexOf("reason")]!)
            .ToArray();

    private static async Task RunLiveTakeAsync(StreamingHost host, CancellationToken cancellation)
    {
        var backend = new SequencedBackend([TestInference, "bonjour"]);
        using var engine = new TranscriptionEngine(
            host,
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 1),
            new RecordingClipboardWriter(),
            captureTail: TestCaptureTail);
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StatusChanged += status =>
        {
            if (status == Loc.Get("Status_Ready")) ready.TrySetResult();
        };
        engine.TranscriptionFinished += result => finished.TrySetResult(result);

        Assert.Equal(ToggleResult.Started, engine.RequestToggle());
        await backend.AllCallsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
        Assert.Equal(ToggleResult.Stopped, engine.RequestToggle());
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
    }

    [Fact]
    public async Task ADisabledCorpusSaysSo()
    {
        var host = new StreamingHost();
        host.Telemetry.CorpusEnabled = false;
        using var listener = new TestEventListener("Deckle-Whisp");

        await RunLiveTakeAsync(host, TestContext.Current.CancellationToken);

        Assert.Equal(["corpus_disabled"], SkipReasons(listener));
    }

    // The ASR line is written; only the rewrite line has no profile to come from.
    [Fact]
    public async Task ATakeWithoutARewriteProfileSaysSoAfterItsAsrLine()
    {
        var host = new StreamingHost();
        host.Telemetry.CorpusEnabled = true;
        host.Telemetry.RecordAudioCorpus = false;
        using var listener = new TestEventListener("Deckle-Whisp");

        await RunLiveTakeAsync(host, TestContext.Current.CancellationToken);

        Assert.Equal(["no_profile"], SkipReasons(listener));
        Assert.Single(listener.Events, e => e.EventName == nameof(DeckleWhispSource.CorpusAsrRecorded));
    }

    [Fact]
    public async Task AFileRunSaysSo()
    {
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        var host = new StreamingHost();
        host.Telemetry.CorpusEnabled = true;
        string wav = Path.Combine(_root, "source.wav");
        WriteToneWav(wav);
        var backend = new SequencedBackend([TestInference, "un", "deux", "trois", "quatre", "cinq", "six"]);
        using var engine = new TranscriptionEngine(
            host,
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 0),
            new RecordingClipboardWriter(),
            captureTail: TestCaptureTail);
        var finished = new TaskCompletionSource<TranscriptionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.TranscriptionFinished += result => finished.TrySetResult(result);
        using var listener = new TestEventListener("Deckle-Whisp");

        Assert.Equal(1, engine.EnqueueFileTranscriptions([wav]));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation);

        Assert.Equal(["file_run"], SkipReasons(listener));
    }

    [Fact]
    public async Task TheSkipEventCarriesTheReasonAndNothingElse()
    {
        var host = new StreamingHost();
        using var listener = new TestEventListener("Deckle-Whisp");

        await RunLiveTakeAsync(host, TestContext.Current.CancellationToken);

        EventWrittenEventArgs skipped = Assert.Single(
            listener.Events, e => e.EventName == nameof(DeckleWhispSource.CorpusRecordSkipped));
        Assert.Equal(EventLevel.Verbose, skipped.Level);
        Assert.Equal(["reason"], skipped.PayloadNames!);
    }

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
