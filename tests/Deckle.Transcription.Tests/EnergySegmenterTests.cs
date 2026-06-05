using System.Collections.Generic;
using Deckle.Audio;
using Deckle.Transcription.Streaming;
using Xunit;

namespace Deckle.Transcription.Tests;

// EnergySegmenter is internal sealed in Deckle.Transcription; access from the
// test project goes through InternalsVisibleTo declared in
// Deckle.Transcription.csproj. Pure, deterministic tests: feed it synthetic
// frame sequences (50 ms each, voiced or silent) and assert emitted utterances,
// without microphone or threading.
//
// Reference points with TestSettings (frame = 50 ms):
//   hangover 400 ms = 8 frames · margin 150 ms = 3 frames · min 250 ms = 5 frames
//   (voiced extent). The ramp is parked far above any test length, so the
//   hangover stays at HangoverMaxMs throughout — the rampless legacy behaviour
//   the early tests were written against.
[Trait("Category", "unit")]
public class EnergySegmenterTests
{
    private const int FrameSamples = 800; // 50 ms @ 16 kHz

    // RMS clearly above/below the -45 dBFS threshold (≈ 0.0056 linear).
    private static CaptureFrame Frame(bool voiced)
        => new(new float[FrameSamples], voiced ? 0.1f : 0.0f);

    // Settings used by tests that want the simple, ramp-inactive behaviour:
    // Min == Max means RequiredHangoverFrames stays at 8 regardless of length,
    // and the ramp anchors are pushed far past any synthesis length.
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
        var seg = new EnergySegmenter(TestSettings(), got.Add);

        PushN(seg, voiced: false, 20);
        seg.Flush();

        Assert.Empty(got);
    }

    [Fact]
    public void ShortBurstBelowMinIsDroppedAsBlip()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), got.Add);

        // 3 voiced frames (150 ms) < min 250 ms → dropped when the hangover expires.
        PushN(seg, voiced: true, 3);
        PushN(seg, voiced: false, 8);

        Assert.Empty(got);
    }

    [Fact]
    public void VoicedThenSilenceEmitsOneUtteranceTrimmedToMargin()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), got.Add);

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
        var seg = new EnergySegmenter(TestSettings(), got.Add);

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
        var seg = new EnergySegmenter(TestSettings(), got.Add);

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
            MinUtteranceMs      = 50,
        };
        var seg = new EnergySegmenter(settings, got.Add);

        PushN(seg, voiced: true, 25);  // length 25 ≥ 20 → at the floor
        PushN(seg, voiced: false, 3);  // 3 ≥ 2 → cuts

        Assert.Single(got);
    }

    [Fact]
    public void HangoverStaysAtMaxBeforeRamp()
    {
        var got = new List<Utterance>();
        // Same max/min as the previous test, but the ramp anchors are pushed
        // past any test length. A 5-frame pause (intra-phrase) must NOT split.
        var settings = new EnergySegmenterSettings
        {
            HangoverMaxMs       = 400,
            HangoverMinMs       = 100,
            HangoverRampStartMs = 5_000,
            HangoverRampEndMs   = 6_000,
            MinUtteranceMs      = 50,
        };
        var seg = new EnergySegmenter(settings, got.Add);

        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 5);  // 5 < hangover max 8 → does not split
        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 8);  // real end

        Assert.Single(got);
    }

    [Fact]
    public void FlushMidWordKeepsInProgressSpeech()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), got.Add);

        PushN(seg, voiced: true, 10); // Stop en pleine parole, aucun silence
        seg.Flush();

        var u = Assert.Single(got);
        Assert.Equal(10, FrameCount(u)); // whole buffer kept (no tail)
    }

    [Fact]
    public void FlushWithNoOpenUtteranceEmitsNothing()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(TestSettings(), got.Add);

        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 8); // → one utterance emitted, back to Silence
        seg.Flush();                  // rien d'ouvert → no-op

        Assert.Single(got);
    }
}
