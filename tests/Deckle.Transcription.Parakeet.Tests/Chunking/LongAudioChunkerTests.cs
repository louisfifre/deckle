using Xunit;

namespace Deckle.Transcription.Parakeet.Tests;

[Trait("Category", "unit")]
public sealed class LongAudioChunkerTests
{
    private const int Rate = LongAudioChunker.SampleRate;

    // Deterministic loud noise: a constant tone would make every region equally loud.
    private static float[] Noise(int length, int seed = 1)
    {
        var random = new Random(seed);
        var buffer = new float[length];
        for (int i = 0; i < length; i++) buffer[i] = (float)(random.NextDouble() * 0.8 - 0.4);
        return buffer;
    }

    [Fact]
    public void Split_EmptyBuffer_ReturnsNoChunk()
    {
        Assert.Empty(LongAudioChunker.Split(ReadOnlySpan<float>.Empty));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5 * Rate)]
    [InlineData(30 * Rate)]
    public void Split_InputUpToLimit_PassesThroughAsOneChunk(int length)
    {
        var chunks = LongAudioChunker.Split(Noise(length));

        Assert.Equal(new AudioChunk(0, length), Assert.Single(chunks));
    }

    [Fact]
    public void Split_PauseInSearchWindow_CutsInsideThePause()
    {
        float[] audio = Noise(45 * Rate);
        // 400 ms of silence at 22.0-22.4 s: inside the 20-30 s search window.
        int silenceStart = (int)(22.0 * Rate);
        Array.Clear(audio, silenceStart, (int)(0.4 * Rate));

        var chunks = LongAudioChunker.Split(audio);

        Assert.Equal(2, chunks.Count);
        int cut = chunks[0].End;
        Assert.InRange(cut, silenceStart, silenceStart + (int)(0.4 * Rate));
    }

    [Fact]
    public void Split_NoPauseAnywhere_StillRespectsTheLimit()
    {
        var chunks = LongAudioChunker.Split(Noise(75 * Rate));

        Assert.All(chunks, c => Assert.InRange(c.Length, 1, LongAudioChunker.MaxChunkSamples));
        Assert.True(chunks.Count >= 3);
    }

    [Theory]
    [InlineData(30 * Rate + 1)]
    [InlineData(61 * Rate)]
    [InlineData(95 * Rate + 123)]
    public void Split_LongInput_ConservesEverySampleExactlyOnce(int length)
    {
        var chunks = LongAudioChunker.Split(Noise(length, seed: length));

        int expectedOffset = 0;
        foreach (var chunk in chunks)
        {
            Assert.Equal(expectedOffset, chunk.Offset); // no gap, no overlap
            Assert.InRange(chunk.Length, 1, LongAudioChunker.MaxChunkSamples);
            expectedOffset = chunk.End;
        }
        Assert.Equal(length, expectedOffset);
    }

    [Fact]
    public void Split_PauseBeforeSearchWindow_IsIgnored()
    {
        float[] audio = Noise(40 * Rate);
        // Silence at 10 s, outside the last 10 s before the 30 s limit: the cut must not go there.
        Array.Clear(audio, 10 * Rate, Rate);

        var chunks = LongAudioChunker.Split(audio);

        Assert.True(chunks[0].End >= 20 * Rate);
    }
}
