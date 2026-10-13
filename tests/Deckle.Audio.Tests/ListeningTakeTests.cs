using System;
using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

// Asserted against TranscriptionPreprocessor itself rather than fixed numbers,
// so the tests follow any re-tune of the DSP.
[Trait("Category", "unit")]
public class ListeningTakeTests
{
    private const double SampleRate = TranscriptionPreprocessor.SampleRate;

    // A quiet -35 dBFS RMS sine, so makeup gain, compressor and high-pass make
    // the processed signal clearly differ from the raw one.
    private static float[] QuietSine()
    {
        double amp = Math.Sqrt(2.0) * Math.Pow(10.0, -35.0 / 20.0);
        int n = (int)SampleRate;
        var x = new float[n];
        double w = 2.0 * Math.PI * 440.0 / SampleRate;
        for (int i = 0; i < n; i++) x[i] = (float)(amp * Math.Sin(w * i));
        return x;
    }

    private static PreprocessingSettings Chain() => new()
    {
        Enabled           = true,
        HighPassEnabled   = true,
        CompressorEnabled = true,
        LimiterEnabled    = true,
        MaxMakeupGainDb   = 24f,
        TargetRmsDbfs     = -20f,
    };

    [Fact]
    public void StrengthZeroReturnsTheRawSamplesAsANewArray()
    {
        var take = new ListeningTake(QuietSine());

        float[] rendered = take.Render(0.0, Chain());

        Assert.Equal(take.Raw, rendered);
        Assert.NotSame(take.Raw, rendered);
    }

    [Fact]
    public void StrengthOneEqualsTheDictationPreprocessing()
    {
        var raw = QuietSine();
        var take = new ListeningTake(raw);

        float[] expected = TranscriptionPreprocessor.Process(raw, Chain()).Pcm;
        float[] rendered = take.Render(1.0, Chain());

        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void StrengthQuarterLiesAQuarterOfTheWayFromRawToProcessed()
    {
        var raw = QuietSine();
        var take = new ListeningTake(raw);
        float[] processed = TranscriptionPreprocessor.Process(raw, Chain()).Pcm;

        float[] rendered = take.Render(0.25, Chain());

        // Guard: the chain must actually change the signal for the blend to mean something.
        Assert.NotEqual(raw, processed);
        for (int i = 0; i < raw.Length; i++)
        {
            double expected = raw[i] + (processed[i] - raw[i]) * 0.25;
            Assert.True(Math.Abs(rendered[i] - expected) <= 1e-6,
                $"sample {i}: {rendered[i]} vs {expected}");
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void RenderNeverMutatesTheRawTake(double strength)
    {
        var take = new ListeningTake(QuietSine());
        var before = (float[])take.Raw.Clone();

        take.Render(strength, Chain());

        Assert.Equal(before, take.Raw);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void RenderKeepsTheTakeLength(double strength)
    {
        var take = new ListeningTake(QuietSine());

        Assert.Equal(take.Raw.Length, take.Render(strength, Chain()).Length);
    }

    [Fact]
    public void StrengthOutsideZeroToOneIsClamped()
    {
        var take = new ListeningTake(QuietSine());

        Assert.Equal(take.Render(1.0, Chain()), take.Render(1.5, Chain()));
        Assert.Equal(take.Render(0.0, Chain()), take.Render(-0.5, Chain()));
    }
}
