using Xunit;
using static Deckle.Transcription.Tests.LiveTranscriptionPipelineTests;

namespace Deckle.Transcription.Tests;

// A file transcribed for the Transcribe and rewrite window: the raw text comes
// back to the caller, utterance by utterance then whole, and nothing is
// written beside the audio. A real engine runs a short tone file on a
// scripted backend.
[Trait("Category", "integration")]
public sealed class FileToTextTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, $"file-to-text-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static TranscriptionEngine NewEngine(SequencedBackend backend) =>
        new(new StreamingHost(),
            backend,
            new ScriptedMicrophoneCapture(utteranceCount: 0),
            new RecordingClipboardWriter(),
            captureTail: TestCaptureTail);

    [Fact]
    public async Task TheTextComesBackAndNoFileIsWritten()
    {
        string wav = Path.Combine(_root, "source.wav");
        ToneWav.Write(wav);
        var backend = new SequencedBackend([TestInference, "bonjour", "deux", "trois"]);
        using var engine = NewEngine(backend);

        string? text = await engine.TranscribeFileToTextAsync(wav)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Equal(["source.wav"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task EachUtteranceReachesTheFeedBeforeTheWholeText()
    {
        string wav = Path.Combine(_root, "source.wav");
        ToneWav.Write(wav);
        var backend = new SequencedBackend([TestInference, "bonjour", "deux", "trois"]);
        using var engine = NewEngine(backend);
        var fed = new List<string>();

        string? text = await engine.TranscribeFileToTextAsync(wav, u => { lock (fed) fed.Add(u.Text); })
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.NotEmpty(fed);
        lock (fed)
            Assert.All(fed, piece => Assert.Contains(piece, text));
    }

    [Fact]
    public async Task AnUnreadableFileReturnsNull()
    {
        var backend = new SequencedBackend([TestInference]);
        using var engine = NewEngine(backend);

        string? text = await engine.TranscribeFileToTextAsync(Path.Combine(_root, "missing.wav"))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Null(text);
        Assert.False(engine.IsBusy);
    }
}

// One second of 440 Hz then half a second of silence, mono 16 kHz PCM.
internal static class ToneWav
{
    public static void Write(string path)
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
