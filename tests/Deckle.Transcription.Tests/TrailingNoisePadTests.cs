using System;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "unit")]
public class TrailingNoisePadTests
{
    private static float[] Speech(int length)
    {
        var samples = new float[length];
        Array.Fill(samples, 0.1f);
        return samples;
    }

    [Fact]
    public void ThePadAddsExactlyItsLengthAfterTheInput()
    {
        float[] padded = TrailingNoisePad.Append(Speech(1_000));

        Assert.Equal(1_000 + TrailingNoisePad.PadSamples, padded.Length);
        Assert.Equal(TrailingNoisePad.PadMs, TrailingNoisePad.PadSamples * 1000 / TrailingNoisePad.SampleRate);
    }

    [Fact]
    public void TheInputSamplesAreKeptUnchangedAtTheStart()
    {
        float[] input = Speech(1_000);
        float[] copy = (float[])input.Clone();

        float[] padded = TrailingNoisePad.Append(input);

        Assert.Equal(copy, input);
        Assert.Equal(copy, padded[..input.Length]);
    }

    [Fact]
    public void ThePadLevelMatchesTheTargetRms()
    {
        float[] padded = TrailingNoisePad.Append(Speech(100));

        var (rmsDbfs, _) = SampleLevels.Measure(padded.AsSpan(100));

        Assert.InRange(rmsDbfs, TrailingNoisePad.TargetRmsDbfs - 0.5, TrailingNoisePad.TargetRmsDbfs + 0.5);
    }

    [Fact]
    public void ThePadIsNeverDigitalSilence()
    {
        float[] padded = TrailingNoisePad.Append(Array.Empty<float>());

        Assert.Contains(padded, sample => sample != 0f);
    }

    [Fact]
    public void TheSameInputAlwaysGivesTheSameOutput()
    {
        float[] first = TrailingNoisePad.Append(Speech(500));
        float[] second = TrailingNoisePad.Append(Speech(500));

        Assert.Equal(first, second);
    }
}
