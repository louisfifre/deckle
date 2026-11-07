using System.Linq;
using Xunit;

namespace Deckle.Vad.Tests;

// The edge trim of a monolithic take, on synthetic probability sequences: the
// spans come from the same Compute the model feeds, so no model is needed.
// Frame math at 16 kHz: window = 512 samples; pad 30 ms = 480 samples;
// the shared floor of 250 ms = 4000 samples drops a span under about 8 windows;
// the edge floor of 50 ms = 800 samples keeps any span of two windows or more.
[Trait("Category", "unit")]
public class SpeechEdgesTests
{
    private const int W = SileroSpeechTimestamps.WindowSamples;
    private const int Pad = 480;

    // The shared floor the engine passes in: the shipped 250 ms.
    private static readonly SileroVadOptions Shared = SileroVadOptions.Default;

    private static float[] Probs(params (float value, int count)[] runs)
        => runs.SelectMany(r => Enumerable.Repeat(r.value, r.count)).ToArray();

    // Each sample holds its own index, so a slice says where it came from.
    private static float[] Samples(int windows)
        => Enumerable.Range(0, windows * W).Select(i => (float)i).ToArray();

    private static float[] EdgeTrim(float[] probs, SileroVadOptions options)
    {
        float[] samples = Samples(probs.Length);
        var segments = SileroSpeechTimestamps.Compute(probs, samples.Length, SpeechEdges.OptionsFor(options));
        return SpeechEdges.Slice(samples, segments);
    }

    // What the edge trim returned before the floor was lowered: same slicing,
    // shared options. Used to show the word was lost, so a test cannot pass
    // on a sequence the old code handled too.
    private static float[] SharedFloorTrim(float[] probs)
    {
        float[] samples = Samples(probs.Length);
        var segments = SileroSpeechTimestamps.Compute(probs, samples.Length, Shared);
        return SpeechEdges.Slice(samples, segments);
    }

    [Fact]
    public void VoiceInTheMiddleKeepsItsSpanWithPaddingAndDropsTheOutsideSilence()
    {
        float[] probs = Probs((0.0f, 10), (0.9f, 40), (0.0f, 10));

        float[] kept = EdgeTrim(probs, Shared);

        Assert.Equal(10 * W - Pad, kept[0]);
        Assert.Equal(40 * W + 2 * Pad, kept.Length);
    }

    [Fact]
    public void AShortWordAtTheStartIsKept()
    {
        // 4 windows (128 ms) of speech, a pause, then a long sentence.
        float[] probs = Probs((0.9f, 4), (0.0f, 12), (0.9f, 40), (0.0f, 6));

        // The shared floor loses the word: the slice starts at the sentence.
        Assert.True(SharedFloorTrim(probs)[0] >= 16 * W - Pad);

        float[] kept = EdgeTrim(probs, Shared);

        Assert.Equal(0f, kept[0]); // starts at the word (padding clamps at 0)
    }

    [Fact]
    public void AShortWordAtTheEndIsKept()
    {
        float[] probs = Probs((0.0f, 4), (0.9f, 40), (0.0f, 12), (0.9f, 4), (0.0f, 6));
        int wordEnd = (4 + 40 + 12 + 4) * W;

        float[] lostWord = SharedFloorTrim(probs);
        Assert.True(lostWord[^1] < wordEnd - W); // the shared floor ends at the sentence

        float[] kept = EdgeTrim(probs, Shared);

        Assert.Equal(wordEnd + Pad - 1, kept[^1]); // ends at the word plus padding
    }

    [Fact]
    public void ASpanOfASingleWindowIsStillDropped()
    {
        // 32 ms, under the edge floor: a click, not a word.
        float[] probs = Probs((0.0f, 6), (0.9f, 1), (0.0f, 12), (0.9f, 40), (0.0f, 6));

        float[] kept = EdgeTrim(probs, Shared);

        Assert.True(kept[0] >= 19 * W - Pad);
    }

    [Fact]
    public void SilenceOnlyYieldsAnEmptySlice()
    {
        Assert.Empty(EdgeTrim(Probs((0.0f, 30)), Shared));
    }

    [Fact]
    public void TheEdgeOptionsLowerTheFloorAndKeepEveryOtherValue()
    {
        var options = new SileroVadOptions
        {
            Threshold = 0.6f, MinSpeechDurationMs = 300, MinSilenceDurationMs = 150, SpeechPadMs = 40,
        };

        SileroVadOptions edge = SpeechEdges.OptionsFor(options);

        Assert.Equal(SpeechEdges.MinSpeechMs, edge.MinSpeechDurationMs);
        Assert.Equal(options with { MinSpeechDurationMs = SpeechEdges.MinSpeechMs }, edge);
    }

    [Fact]
    public void AFloorAlreadyBelowTheEdgeFloorIsKept()
    {
        var options = new SileroVadOptions { MinSpeechDurationMs = 20 };

        Assert.Equal(20, SpeechEdges.OptionsFor(options).MinSpeechDurationMs);
    }
}
