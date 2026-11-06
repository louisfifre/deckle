using System.Collections.Generic;
using Deckle.Audio;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// Reference points with TestSettings (frame = 50 ms):
//   hangover 400 ms = 8 frames · margin 150 ms = 3 frames · min 250 ms = 5 frames
//   (voiced extent).
[Trait("Category", "unit")]
public class EnergySegmenterTests
{
    private const int FrameSamples = 800; // 50 ms @ 16 kHz

    private static readonly double VoiceLevel = new CaptureSettings().VoiceLevelDbfs;

    // The shipped minimum speech: 250 ms = 5 frames of voiced extent.
    private static readonly int MinSpeechMs = new StreamingSettings().MinSpeechMs;

    // RMS clearly above the entry level and below the exit level of the
    // default voice level (−45 dBFS ≈ 0.0056 linear).
    private static CaptureFrame Frame(bool voiced)
        => new(new float[FrameSamples], voiced ? 0.1f : 0.0f);

    // Settings used by tests that want the simple, ramp-inactive behaviour:
    // Min == Max means RequiredHangoverFrames stays at 8 regardless of length,
    // and the ramp anchors are pushed far past any synthesis length.
    private static CaptureFrame FrameAtDbfs(double dbfs)
        => new(new float[FrameSamples], (float)System.Math.Pow(10.0, dbfs / 20.0));

    private static EnergySegmenterSettings TestSettings()
        => new()
        {
            HangoverMaxMs       = 400,
            HangoverMinMs       = 400,
            HangoverRampStartMs = 1_000_000,
            HangoverRampEndMs   = 1_000_000,
        };

    private static void PushN(EnergySegmenter seg, bool voiced, int n)
    {
        for (int i = 0; i < n; i++) seg.Push(Frame(voiced));
    }

    private static int FrameCount(Utterance u) => u.Samples.Length / FrameSamples;

    [Fact]
    public void PureSilenceEmitsNothing()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), VoiceLevel, MinSpeechMs, got.Add);

        PushN(seg, voiced: false, 20);
        seg.Flush();

        Assert.Empty(got);
    }

    [Fact]
    public void ShortBurstBelowMinIsDroppedAsBlip()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), VoiceLevel, MinSpeechMs, got.Add);

        // 3 voiced frames (150 ms) < min 250 ms → dropped when the hangover expires.
        PushN(seg, voiced: true, 3);
        PushN(seg, voiced: false, 8);

        Assert.Empty(got);
    }

    [Fact]
    public void VoicedThenSilenceEmitsOneUtteranceTrimmedToMargin()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), VoiceLevel, MinSpeechMs, got.Add);

        // 10 voiced frames (extent ≥ min), then 8 silences → end on silence.
        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 8);

        var u = Assert.Single(got);
        // Kept = 10 voiced frames + 3 margin; the 5 silences beyond are dropped.
        Assert.Equal(13, FrameCount(u));
        Assert.Equal(0, u.Index);
        Assert.Equal(0.0, u.StartSec, 3);
        Assert.Equal(0.65, u.EndSec, 3); // 13 × 50 ms
    }

    [Fact]
    public void IntraPhrasePauseShorterThanHangoverDoesNotSplit()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), VoiceLevel, MinSpeechMs, got.Add);

        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 5);  // pause < hangover (8) → does not split
        PushN(seg, voiced: true, 10);  // resume
        PushN(seg, voiced: false, 8);  // real end

        Assert.Single(got);
    }

    [Fact]
    public void LongSilenceSeparatesTwoUtterances()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), VoiceLevel, MinSpeechMs, got.Add);

        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 8);   // → U0 (global frames 0..17)
        PushN(seg, voiced: false, 10);  // inter-utterance silence, dropped (18..27)
        PushN(seg, voiced: true, 10);   // U1 starts at global frame 28
        PushN(seg, voiced: false, 8);   // → U1

        Assert.Equal(2, got.Count);
        Assert.Equal(0, got[0].Index);
        Assert.Equal(1, got[1].Index);
        Assert.Equal(1.40, got[1].StartSec, 3); // 28 × 50 ms
    }

    [Fact]
    public void HangoverShrinksPastRampSoShortPauseCutsLongUtterance()
    {
        var got = new List<Utterance>();
        // Synthesis-sized ramp: max 400 ms (8 frames) → min 100 ms (2 frames),
        // ramp 500 ms (10 frames) → 1000 ms (20 frames). Past 20 frames the
        // required hangover is the floor (2 frames), so a 3-frame pause cuts
        // where it normally wouldn't.
        var settings = new EnergySegmenterSettings
        {
            HangoverMaxMs       = 400,
            HangoverMinMs       = 100,
            HangoverRampStartMs = 500,
            HangoverRampEndMs   = 1_000,
        };
        var seg = new EnergySegmenter(settings, VoiceLevel, minSpeechMs: 50, got.Add);

        PushN(seg, voiced: true, 25);  // length 25 ≥ 20 → at the floor
        PushN(seg, voiced: false, 3);  // 3 ≥ 2 → cuts

        Assert.Single(got);
    }

    [Fact]
    public void BezierControlPointsShapeIntermediateHangover()
    {
        var linear = new EnergySegmenterSettings
        {
            HangoverMaxMs       = 5_000,
            HangoverMinMs       = 500,
            HangoverRampStartMs = 1_000,
            HangoverRampEndMs   = 5_000,
            HangoverCurveX1     = 0.0,
            HangoverCurveY1     = 0.0,
            HangoverCurveX2     = 1.0,
            HangoverCurveY2     = 1.0,
        };

        var lateDrop = new EnergySegmenterSettings
        {
            HangoverMaxMs       = linear.HangoverMaxMs,
            HangoverMinMs       = linear.HangoverMinMs,
            HangoverRampStartMs = linear.HangoverRampStartMs,
            HangoverRampEndMs   = linear.HangoverRampEndMs,
            HangoverCurveX1     = 0.85,
            HangoverCurveY1     = 0.10,
            HangoverCurveX2     = 0.90,
            HangoverCurveY2     = 0.25,
        };

        var linearSeg = new EnergySegmenter(linear, VoiceLevel, minSpeechMs: 50, _ => { });
        var lateSeg = new EnergySegmenter(lateDrop, VoiceLevel, minSpeechMs: 50, _ => { });

        PushN(linearSeg, voiced: true, 60);
        PushN(lateSeg, voiced: true, 60);

        Assert.Equal(55, linearSeg.Snapshot().RequiredHangoverFrames);
        // The shaped late-drop curve at p=0.5 yields a far longer hangover than the
        // linear baseline (55) — pin the exact value, not merely "greater than".
        Assert.Equal(92, lateSeg.Snapshot().RequiredHangoverFrames);
    }

    [Fact]
    public void HangoverStaysAtMaxBeforeRamp()
    {
        var got = new List<Utterance>();
        // The ramp anchors sit past any test length, so a 5-frame pause
        // (intra-phrase) must NOT split.
        var settings = new EnergySegmenterSettings
        {
            HangoverMaxMs       = 400,
            HangoverMinMs       = 100,
            HangoverRampStartMs = 5_000,
            HangoverRampEndMs   = 6_000,
        };
        var seg = new EnergySegmenter(settings, VoiceLevel, minSpeechMs: 50, got.Add);

        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 5);  // 5 < hangover max 8 → does not split
        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 8);  // real end

        Assert.Single(got);
    }

    // The entry margin gates the opening of an utterance: with a positive
    // margin, a frame between the level and the entry does not open one, a
    // frame at the entry does. Levels come from a detector with the same margin.
    [Fact]
    public void WithAPositiveMarginOnlyAFrameAtTheEntryOpensAnUtterance()
    {
        const double margin = 3.0;
        var detector = new VoiceDetector(VoiceLevel, margin);
        double between = (detector.ExitDbfs + detector.EntryDbfs) / 2;
        var seg = new EnergySegmenter(
            TestSettings(), VoiceLevel, MinSpeechMs, _ => { }, entryMarginDb: margin);

        seg.Push(FrameAtDbfs(between));
        Assert.Equal(0, seg.Snapshot().CurrentUtteranceFrames);

        seg.Push(FrameAtDbfs(detector.EntryDbfs));
        Assert.Equal(1, seg.Snapshot().CurrentUtteranceFrames);
    }

    [Fact]
    public void FlushMidWordKeepsInProgressSpeech()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), VoiceLevel, MinSpeechMs, got.Add);

        PushN(seg, voiced: true, 10); // Stop en pleine parole, aucun silence
        seg.Flush();

        var u = Assert.Single(got);
        Assert.Equal(10, FrameCount(u)); // whole buffer kept (no tail)
    }

    [Fact]
    public void FlushWithNoOpenUtteranceEmitsNothing()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), VoiceLevel, MinSpeechMs, got.Add);

        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 8); // → one utterance emitted, back to Silence
        seg.Flush();                  // rien d'ouvert → no-op

        Assert.Single(got);
    }
}
