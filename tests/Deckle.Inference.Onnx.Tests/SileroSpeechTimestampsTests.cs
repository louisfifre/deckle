using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Deckle.Inference.Onnx.Tests;

// SileroSpeechTimestamps is the pure, model-free half of the Silero VAD port —
// the get_speech_timestamps hysteresis state machine. We drive it with synthetic
// per-window probability sequences (no ONNX model involved) and assert the padded
// [start,end) speech ranges it derives. Behaviour, not internals: these pin the
// reference semantics (min-speech drop, min-silence bridge, hysteresis dead-band,
// padding) so a refactor of the loop stays honest.
//
// Frame math at 16 kHz: window = 512 samples; min-speech 250 ms = 4000 samples
// (> 7.8 windows), min-silence 100 ms = 1600 samples (>= 4 windows to close),
// pad 30 ms = 480 samples.
[Trait("Category", "unit")]
public class SileroSpeechTimestampsTests
{
    private const int W = 512;

    // Builds a probability sequence from (value, repeat-count) runs.
    private static float[] Probs(params (float value, int count)[] runs)
        => runs.SelectMany(r => Enumerable.Repeat(r.value, r.count)).ToArray();

    private static List<(int start, int end)> Compute(float[] probs)
        => SileroSpeechTimestamps
            .Compute(probs, probs.Length * W, SileroVadOptions.Default)
            .Select(s => (s.StartSample, s.EndSample))
            .ToList();

    [Fact]
    public void PureSilenceYieldsNoSpeech()
    {
        Assert.Empty(Compute(Probs((0.0f, 12))));
    }

    [Fact]
    public void ContinuousSpeechIsOneSpanCoveringTheBuffer()
    {
        var segs = Compute(Probs((0.9f, 20)));   // 20 windows = 10240 samples
        Assert.Single(segs);
        Assert.Equal((0, 10240), segs[0]);       // start clamps at 0, end at length
    }

    [Fact]
    public void ShortBlipBelowMinSpeechIsDropped()
    {
        // 3 windows (1536 samples) of speech is under the 4000-sample min-speech;
        // once enough silence closes it, the span is discarded.
        Assert.Empty(Compute(Probs((0.9f, 3), (0.0f, 6))));
    }

    [Fact]
    public void LongSilenceSplitsIntoTwoSpans()
    {
        // speech | silence longer than min-silence | speech.
        var segs = Compute(Probs((0.9f, 10), (0.1f, 5), (0.9f, 10)));
        Assert.Equal(2, segs.Count);
        Assert.Equal((0, 5600), segs[0]);
        Assert.Equal((7200, 12800), segs[1]);
    }

    [Fact]
    public void ShortSilenceBelowMinSilenceDoesNotSplit()
    {
        // A 2-window gap (1024 samples) is under the 1600-sample min-silence: the
        // span stays open across it.
        var segs = Compute(Probs((0.9f, 10), (0.1f, 2), (0.9f, 10)));
        Assert.Single(segs);
        Assert.Equal((0, 11264), segs[0]);
    }

    [Fact]
    public void HysteresisDeadBandKeepsTheSpanOpen()
    {
        // 0.4 sits in the dead-band (release 0.35 <= 0.4 < threshold 0.5): it
        // neither confirms nor closes, so the span bridges it.
        var segs = Compute(Probs((0.9f, 10), (0.4f, 3), (0.9f, 10)));
        Assert.Single(segs);
        Assert.Equal((0, 11776), segs[0]);
    }
}
