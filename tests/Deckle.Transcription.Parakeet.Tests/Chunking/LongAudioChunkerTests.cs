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
    [InlineData(LongAudioChunker.MaxChunkSamples)]
    public void Split_InputUpToLimit_PassesThroughAsOneChunk(int length)
    {
        var chunks = LongAudioChunker.Split(Noise(length));

        Assert.Equal(new AudioChunk(0, length), Assert.Single(chunks));
    }

    [Fact]
    public void Split_PauseInSearchWindow_CutsInsideThePause()
    {
        float[] audio = Noise(135 * Rate);
        // 400 ms of silence at 112.0-112.4 s: inside the 110-120 s search window.
        int silenceStart = (int)(112.0 * Rate);
        Array.Clear(audio, silenceStart, (int)(0.4 * Rate));

        var chunks = LongAudioChunker.Split(audio);

        Assert.Equal(2, chunks.Count);
        int cut = chunks[0].End;
        Assert.InRange(cut, silenceStart, silenceStart + (int)(0.4 * Rate));
    }

    [Fact]
    public void Split_NoPauseAnywhere_StillRespectsTheLimit()
    {
        var chunks = LongAudioChunker.Split(Noise(300 * Rate));

        Assert.All(chunks, c => Assert.InRange(c.Length, 1, LongAudioChunker.MaxChunkSamples));
        Assert.True(chunks.Count >= 3);
    }

    [Theory]
    [InlineData(LongAudioChunker.MaxChunkSamples + 1)]
    [InlineData(241 * Rate)]
    [InlineData(335 * Rate + 123)]
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
        float[] audio = Noise(150 * Rate);
        // Silence at 60 s, outside the last 10 s before the 120 s limit: the cut must not go there.
        Array.Clear(audio, 60 * Rate, Rate);

        var chunks = LongAudioChunker.Split(audio);

        Assert.True(chunks[0].End >= 110 * Rate);
    }
}
