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
// Reference points with default settings (frame = 50 ms):
//   hangover 400 ms = 8 frames · margin 150 ms = 3 frames · min 250 ms = 5 frames
//   (voiced extent) · max 25,000 ms = 500 frames.
[Trait("Category", "unit")]
public class EnergySegmenterTests
{
    private const int FrameSamples = 800; // 50 ms @ 16 kHz

    // RMS clearly above/below the -45 dBFS threshold (≈ 0.0056 linear).
    private static CaptureFrame Frame(bool voiced)
        => new(new float[FrameSamples], voiced ? 0.1f : 0.0f);

    private static void PushN(EnergySegmenter seg, bool voiced, int n)
    {
        for (int i = 0; i < n; i++) seg.Push(Frame(voiced));
    }

    private static int FrameCount(Utterance u) => u.Samples.Length / FrameSamples;

    [Fact]
    public void PureSilenceEmitsNothing()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(new EnergySegmenterSettings(), got.Add);

        PushN(seg, voiced: false, 20);
        seg.Flush();

        Assert.Empty(got);
    }

    [Fact]
    public void ShortBurstBelowMinIsDroppedAsBlip()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(new EnergySegmenterSettings(), got.Add);

        // 3 voiced frames (150 ms) < min 250 ms → dropped when the hangover expires.
        PushN(seg, voiced: true, 3);
        PushN(seg, voiced: false, 8);

        Assert.Empty(got);
    }

    [Fact]
    public void VoicedThenSilenceEmitsOneUtteranceTrimmedToMargin()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(new EnergySegmenterSettings(), got.Add);

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
        var seg = new EnergySegmenter(new EnergySegmenterSettings(), got.Add);

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
        var seg = new EnergySegmenter(new EnergySegmenterSettings(), got.Add);

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
    public void MaxDurationForcesFlushAndKeepsCapturing()
    {
        var got = new List<Utterance>();
        // Max reduced to 500 ms (10 frames); min to 50 ms (1 frame) to avoid
        // dropping the tail.
        var settings = new EnergySegmenterSettings { MaxUtteranceMs = 500, MinUtteranceMs = 50 };
        var seg = new EnergySegmenter(settings, got.Add);

        PushN(seg, voiced: true, 25); // parole continue, jamais de silence
        seg.Flush();

        Assert.Equal(3, got.Count);
        Assert.Equal(10, FrameCount(got[0])); // forced flush at max
        Assert.Equal(10, FrameCount(got[1])); // second forced flush
        Assert.Equal(5, FrameCount(got[2]));  // reste, sorti au Flush
    }

    [Fact]
    public void FlushMidWordKeepsInProgressSpeech()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(new EnergySegmenterSettings(), got.Add);

        PushN(seg, voiced: true, 10); // Stop en pleine parole, aucun silence
        seg.Flush();

        var u = Assert.Single(got);
        Assert.Equal(10, FrameCount(u)); // whole buffer kept (no tail)
    }

    [Fact]
    public void FlushWithNoOpenUtteranceEmitsNothing()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(new EnergySegmenterSettings(), got.Add);

        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 8); // → one utterance emitted, back to Silence
        seg.Flush();                  // rien d'ouvert → no-op

        Assert.Single(got);
    }
}
