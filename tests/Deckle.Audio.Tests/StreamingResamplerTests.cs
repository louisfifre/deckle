using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

// Pins the sample-rate conversion to 16 kHz: exact output length, independence
// from how the input is chunked, unit gain in the passband, strong rejection
// above the output's Nyquist, and a bypass when the rates are equal.
public class StreamingResamplerTests
{
    private static float[] Run(int inputRate, int outputRate, float[] input, params int[] chunks)
    {
        var resampler = new StreamingResampler(inputRate, outputRate);
        var output = new List<float>();
        int offset = 0, i = 0;
        while (offset < input.Length)
        {
            int size = chunks.Length == 0 ? input.Length : Math.Min(chunks[i++ % chunks.Length], input.Length - offset);
            output.AddRange(resampler.Process(input.AsSpan(offset, size)).ToArray());
            offset += size;
        }
        output.AddRange(resampler.Flush().ToArray());
        return [.. output];
    }

    private static float[] Sine(double hz, int rate, int count, double amplitude = 0.5)
        => Enumerable.Range(0, count).Select(n => (float)(amplitude * Math.Sin(2 * Math.PI * hz * n / rate))).ToArray();

    private static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (float s in samples) sum += (double)s * s;
        return Math.Sqrt(sum / samples.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(100)]
    [InlineData(4799)]
    [InlineData(48000)]
    [Trait("Category", "unit")]
    public void Flush_From48kTo16k_GivesCeilOfInputOverThree(int inputLength)
    {
        float[] output = Run(48000, 16000, new float[inputLength]);

        Assert.Equal((inputLength + 2) / 3, output.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(44100)]
    [InlineData(50001)]
    [Trait("Category", "unit")]
    public void Flush_From44100To16k_GivesCeilOfScaledLength(int inputLength)
    {
        float[] output = Run(44100, 16000, new float[inputLength]);

        long expected = ((long)inputLength * 16000 + 44099) / 44100;
        Assert.Equal(expected, output.Length);
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [Trait("Category", "unit")]
    public void Process_InIrregularChunks_MatchesASingleCall(int inputRate)
    {
        var random = new Random(1234);
        float[] input = Enumerable.Range(0, 9000).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();

        float[] whole = Run(inputRate, 16000, input);
        float[] chunked = Run(inputRate, 16000, input, 1, 7, 100, 333, 2, 1024, 59);

        Assert.Equal(whole.Length, chunked.Length);
        for (int i = 0; i < whole.Length; i++)
            Assert.True(Math.Abs(whole[i] - chunked[i]) <= 1e-6f, $"sample {i}: {whole[i]} vs {chunked[i]}");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Process_ConstantInput_SettlesToTheSameConstant()
    {
        float[] output = Run(48000, 16000, Enumerable.Repeat(0.4f, 4800).ToArray());

        for (int i = 40; i < output.Length - 40; i++)
            Assert.True(Math.Abs(output[i] - 0.4f) < 1e-5f, $"sample {i}: {output[i]}");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Process_OneKilohertzSine_KeepsItsLevelWithinHalfADecibel()
    {
        float[] output = Run(48000, 16000, Sine(1000, 48000, 48000));

        double ratio = Rms(output.AsSpan(200, output.Length - 400)) / Rms(Sine(1000, 48000, 48000)[600..^600]);
        double decibels = 20 * Math.Log10(ratio);
        Assert.InRange(decibels, -0.5, 0.5);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Process_TwelveKilohertzSine_IsAttenuatedByAtLeastSixtyDecibels()
    {
        float[] input = Sine(12000, 48000, 48000);
        float[] output = Run(48000, 16000, input);

        double decibels = 20 * Math.Log10(Rms(output.AsSpan(200, output.Length - 400)) / Rms(input));
        Assert.True(decibels <= -60, $"attenuation was only {decibels:F1} dB");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Process_EqualRates_PassesSamplesThroughUnchanged()
    {
        var resampler = new StreamingResampler(16000, 16000);
        float[] input = [0.1f, -0.2f, 0.3f, 0.99f];

        Assert.Equal(input, resampler.Process(input).ToArray());
        Assert.True(resampler.Flush().IsEmpty);
    }
}
