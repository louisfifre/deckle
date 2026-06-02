using System.Collections.Generic;
using Deckle.Audio;
using Deckle.Transcription.Streaming;
using Xunit;

namespace Deckle.Tests.Transcription;

// EnergySegmenter est internal sealed dans Deckle.Transcription ; l'accès depuis
// le projet de tests passe par l'InternalsVisibleTo déclaré dans
// Deckle.Transcription.csproj. Tests purs, déterministes : on lui pousse des
// suites de frames synthétiques (50 ms chacune, voisée ou silencieuse) et on
// asserte les utterances émises — sans micro ni threading.
//
// Repères avec les paramètres par défaut (frame = 50 ms) :
//   hangover 400 ms = 8 frames · marge 150 ms = 3 frames · min 250 ms = 5 frames
//   (extent voisé) · max 25 000 ms = 500 frames.
[Trait("Category", "unit")]
public class EnergySegmenterTests
{
    private const int FrameSamples = 800; // 50 ms @ 16 kHz

    // RMS clairement au-dessus / en-dessous du seuil -45 dBFS (≈ 0.0056 linéaire).
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

        // 3 frames voisées (150 ms) < min 250 ms → jeté quand le hangover expire.
        PushN(seg, voiced: true, 3);
        PushN(seg, voiced: false, 8);

        Assert.Empty(got);
    }

    [Fact]
    public void VoicedThenSilenceEmitsOneUtteranceTrimmedToMargin()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(new EnergySegmenterSettings(), got.Add);

        // 10 voisées (extent ≥ min) puis 8 silences → fin sur silence.
        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 8);

        var u = Assert.Single(got);
        // Conservé = 10 voisées + 3 de marge ; les 5 silences au-delà sont jetés.
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
        PushN(seg, voiced: false, 5);  // pause < hangover (8) → ne coupe pas
        PushN(seg, voiced: true, 10);  // reprise
        PushN(seg, voiced: false, 8);  // vraie fin

        Assert.Single(got);
    }

    [Fact]
    public void LongSilenceSeparatesTwoUtterances()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(new EnergySegmenterSettings(), got.Add);

        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 8);   // → U0 (frames globales 0..17)
        PushN(seg, voiced: false, 10);  // silence inter-utterance, jeté (18..27)
        PushN(seg, voiced: true, 10);   // U1 démarre à la frame globale 28
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
        // Max ramené à 500 ms (10 frames) ; min à 50 ms (1 frame) pour ne pas
        // jeter la queue.
        var settings = new EnergySegmenterSettings { MaxUtteranceMs = 500, MinUtteranceMs = 50 };
        var seg = new EnergySegmenter(settings, got.Add);

        PushN(seg, voiced: true, 25); // parole continue, jamais de silence
        seg.Flush();

        Assert.Equal(3, got.Count);
        Assert.Equal(10, FrameCount(got[0])); // flush forcé au max
        Assert.Equal(10, FrameCount(got[1])); // second flush forcé
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
        Assert.Equal(10, FrameCount(u)); // tout le buffer conservé (pas de traîne)
    }

    [Fact]
    public void FlushWithNoOpenUtteranceEmitsNothing()
    {
        var got = new List<Utterance>();
        var seg = new EnergySegmenter(new EnergySegmenterSettings(), got.Add);

        PushN(seg, voiced: true, 10);
        PushN(seg, voiced: false, 8); // → une utterance émise, retour en Silence
        seg.Flush();                  // rien d'ouvert → no-op

        Assert.Single(got);
    }
}
