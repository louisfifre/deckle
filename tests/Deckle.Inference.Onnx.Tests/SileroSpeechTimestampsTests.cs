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

    private static List<(int start, int end)> Compute(float[] probs, SileroVadOptions? options = null)
        => SileroSpeechTimestamps
            .Compute(probs, probs.Length * W, options ?? SileroVadOptions.Default)
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

    [Fact]
    public void TightGapPaddingSplitsTheGapWithoutOverlap()
    {
        // When the inter-span gap is narrower than 2*pad, the reference splits it
        // by halves so the padded spans meet instead of overlapping. At the default
        // 30 ms pad that branch is unreachable (min-silence always leaves a wider
        // gap), so widen the pad to 100 ms (1600 samples → 2*pad = 3200) over the
        // same two-span signal — its raw gap of 2560 now falls under 3200.
        var segs = Compute(
            Probs((0.9f, 10), (0.1f, 5), (0.9f, 10)),
            SileroVadOptions.Default with { SpeechPadMs = 100 });

        // Raw spans (0,5120) and (7680,12800): gap 2560 split by 1280, so the two
        // padded spans meet exactly at 6400 with no overlap.
        Assert.Equal(2, segs.Count);
        Assert.Equal((0, 6400), segs[0]);
        Assert.Equal((6400, 12800), segs[1]);
        Assert.True(segs[0].end <= segs[1].start);
    }

    // ── Boundary cases ──────────────────────────────────────────────────────
    //
    // The tests above sit comfortably on each side of the decision edges; these
    // pin the exact crossing window so an off-by-one in the silence countdown, the
    // min-speech filter, the tail flush, or the hysteresis operators can't slip
    // through. Frame step is 512 samples, so the diffs land just under / just over.

    [Fact]
    public void FourWindowSilenceBridgesBelowMinSilence()
    {
        // The close fires when cur - tempEnd >= min-silence (1600). With tempEnd
        // anchored at the first silent window, a 4-window gap reaches only 1536
        // (< 1600) before speech resumes, so it must NOT split — it brackets the
        // 5-window gap of LongSilenceSplitsIntoTwoSpans on the stay-open side.
        var segs = Compute(Probs((0.9f, 10), (0.1f, 4), (0.9f, 10)));
        Assert.Single(segs);
        Assert.Equal((0, 12288), segs[0]);
    }

    [Fact]
    public void SevenWindowSpeechBelowMinSpeechIsDropped()
    {
        // Closed extent = first silent window = 512*7 = 3584, under the 4000-sample
        // min-speech (strict >), so the span is dropped at close.
        Assert.Empty(Compute(Probs((0.9f, 7), (0.1f, 5))));
    }

    [Fact]
    public void EightWindowSpeechAboveMinSpeechIsKept()
    {
        // One window more: extent 512*8 = 4096 > 4000, so the span survives. Pins
        // the keep/drop edge at window granularity against the case above.
        var segs = Compute(Probs((0.9f, 8), (0.1f, 5)));
        Assert.Single(segs);
        Assert.Equal((0, 4576), segs[0]);   // (0,4096) padded by 480 at the tail
    }

    [Fact]
    public void ShortSpeechTailAtEndOfBufferIsDropped()
    {
        // A late onset that runs to the end of the buffer goes through the tail
        // flush, which applies the same min-speech filter: 4096-2560 = 1536 < 4000,
        // so the trailing blip is dropped (the drop side of the tail flush).
        Assert.Empty(Compute(Probs((0.0f, 5), (0.9f, 3))));
    }

    [Fact]
    public void SpeechTailAtEndOfBufferAboveMinSpeechIsKept()
    {
        // A longer tail clears min-speech and is kept — and its start is padded off
        // a non-zero onset (2560 - 480), exercising the interior start-pad clamp.
        var segs = Compute(Probs((0.0f, 5), (0.9f, 10)));
        Assert.Single(segs);
        Assert.Equal((2080, 7680), segs[0]);
    }

    [Fact]
    public void TriggerThresholdExactlyStartsSpan()
    {
        // p == threshold counts as speech (p >= threshold), so an exactly-0.5 run
        // opens and sustains a span. Pins the >= edge against a > regression.
        var segs = Compute(Probs((0.5f, 20)));
        Assert.Single(segs);
        Assert.Equal((0, 10240), segs[0]);
    }

    [Fact]
    public void ReleaseThresholdExactlyStaysInDeadBand()
    {
        // p == release-threshold (0.35) is NOT silence (close uses p < negThreshold),
        // so it sits in the dead-band and bridges the span. Pins the < edge against
        // a <= regression.
        var segs = Compute(Probs((0.9f, 10), (0.35f, 3), (0.9f, 10)));
        Assert.Single(segs);
        Assert.Equal((0, 11776), segs[0]);
    }
}
