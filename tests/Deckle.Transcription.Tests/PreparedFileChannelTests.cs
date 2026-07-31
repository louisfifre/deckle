using Deckle.Audio;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "regression")]
public sealed class PreparedFileChannelTests
{
    [Fact]
    public async Task ProducerCannotPrepareMoreThanOneFileAhead()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var channel = PreparedFileProducer.CreateChannel();
        int decoded = 0;
        Task producer = PreparedFileProducer.ProduceAsync(
            ["first.m4a", "second.m4a", "third.m4a"],
            channel.Writer,
            path =>
            {
                Interlocked.Increment(ref decoded);
                return new AudioFileDecodeResult(
                    AudioFileDecodeStatus.Decoded,
                    new float[16_000],
                    1);
            },
            _ => throw new InvalidOperationException("No fake decode should fail."),
            (_, exception) => throw exception,
            cancellationToken);

        Assert.True(await channel.Reader.WaitToReadAsync(cancellationToken));
        Assert.Equal(1, Volatile.Read(ref decoded));

        PreparedFileTranscription first =
            await channel.Reader.ReadAsync(cancellationToken);
        Assert.Equal("first.m4a", first.SourcePath);

        Assert.True(await channel.Reader.WaitToReadAsync(cancellationToken));
        Assert.Equal(2, Volatile.Read(ref decoded));

        Assert.Equal(
            "second.m4a",
            (await channel.Reader.ReadAsync(cancellationToken)).SourcePath);
        Assert.True(await channel.Reader.WaitToReadAsync(cancellationToken));
        Assert.Equal(
            "third.m4a",
            (await channel.Reader.ReadAsync(cancellationToken)).SourcePath);
        await producer;
    }

    [Fact]
    public async Task DecoderExceptionSkipsOnlyThatFile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var channel = PreparedFileProducer.CreateChannel();
        var failures = new List<string>();
        Task producer = PreparedFileProducer.ProduceAsync(
            ["first.wav", "broken.wav", "third.wav"],
            channel.Writer,
            path => path == "broken.wav"
                ? throw new InvalidDataException("broken")
                : new AudioFileDecodeResult(
                    AudioFileDecodeStatus.Decoded,
                    new float[800],
                    0.05),
            _ => throw new InvalidOperationException("No categorical failure expected."),
            (path, _) => failures.Add(path),
            cancellationToken);

        var prepared = new List<string>();
        await foreach (PreparedFileTranscription file in
            channel.Reader.ReadAllAsync(cancellationToken))
        {
            prepared.Add(file.SourcePath);
        }
        await producer;

        Assert.Equal(["first.wav", "third.wav"], prepared);
        Assert.Equal(["broken.wav"], failures);
    }
}
