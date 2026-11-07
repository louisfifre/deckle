using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

// Pins the 50 ms cut of the 16 kHz stream: windows of exactly 800 samples, in
// order, with their own RMS, whatever the size of the pushes, and nothing for
// a trailing partial window.
public class SubWindowAssemblerTests
{
    // Window w holds +-0.01 * (w + 1), alternating in sign, so its RMS is that amplitude.
    private static float Amplitude(int window) => 0.01f * (window + 1);

    private static float[] Stream(int length)
        => Enumerable.Range(0, length)
            .Select(i => (i % 2 == 0 ? 1f : -1f) * Amplitude(i / SubWindowAssembler.WindowSamples))
            .ToArray();

    [Theory]
    [InlineData(48)]    // 3 ms
    [InlineData(160)]   // 10 ms
    [InlineData(16000)] // 1 s
    [Trait("Category", "unit")]
    public void Push_AnyChunkSize_EmitsWindowsOf800SamplesInOrderWithTheirRms(int chunk)
    {
        float[] stream = Stream(16000 + 400);
        var windows = new List<(float[] Samples, float Rms)>();
        var assembler = new SubWindowAssembler();

        for (int offset = 0; offset < stream.Length; offset += chunk)
        {
            int size = Math.Min(chunk, stream.Length - offset);
            assembler.Push(stream.AsSpan(offset, size), (w, rms) => windows.Add((w.ToArray(), rms)));
        }

        Assert.Equal(20, windows.Count);
        for (int w = 0; w < windows.Count; w++)
        {
            Assert.Equal(SubWindowAssembler.WindowSamples, windows[w].Samples.Length);
            Assert.Equal(stream.AsSpan(w * 800, 800).ToArray(), windows[w].Samples);
            Assert.Equal(Amplitude(w), windows[w].Rms, precision: 5);
        }
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Push_LessThanAWindow_EmitsNothing()
    {
        int count = 0;
        new SubWindowAssembler().Push(new float[799], (_, _) => count++);

        Assert.Equal(0, count);
    }
}
