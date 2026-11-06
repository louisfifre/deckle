namespace Deckle.Audio;

// ── VoiceDetector ─────────────────────────────────────────────────────────────
//
// The one energy answer to "is this 50 ms frame voice?". Fed the raw linear
// RMS of each frame, it says voiced or silent, with a hysteresis: two levels,
// one to enter voice, a lower one to leave it.
//
// The user's voice level (CaptureSettings.VoiceLevelDbfs) is the EXIT level:
// a voiced run ends when a frame falls below it. The ENTRY level is derived
// above it, EntryMarginDb higher. The anchoring on exit is deliberate: the cut
// behaviour from before the detector existed is kept (everything below the
// level is silence), so a level tuned by ear against it still means the same
// thing, and no room noise sitting between the two levels can hold an
// utterance open once the voice has gone. What the band adds is that a frame
// dipping a little under the entry level, without crossing the exit level,
// stays voiced.
//
// One detector per consumer (the segmenter, the low-audio warning of the
// capture loop), each fed the same RMS sequence and therefore reaching the
// same decisions. Deterministic, allocation-free, no logging. Single-threaded
// by construction: Push and Reset run on the one thread that owns the
// instance, so there is no lock.
public sealed class VoiceDetector
{
    // Interpretation, not a measured value: 3 dB is a narrow band chosen so the
    // entry stays close to the level the user set. It is to be measured
    // against the audio corpus (frames that change side, utterances cut
    // differently) before it is considered settled.
    internal const double EntryMarginDb = 3.0;

    private readonly float _exitRms;
    private readonly float _entryRms;

    public VoiceDetector(double voiceLevelDbfs)
    {
        ExitDbfs  = voiceLevelDbfs;
        EntryDbfs = voiceLevelDbfs + EntryMarginDb;

        // dBFS → linear once, so the per-frame test is a plain comparison.
        _exitRms  = (float)Math.Pow(10.0, ExitDbfs / 20.0);
        _entryRms = (float)Math.Pow(10.0, EntryDbfs / 20.0);
    }

    // The level below which a voiced run ends: the user's voice level.
    public double ExitDbfs { get; }

    // The level a silent frame must reach to open a voiced run.
    public double EntryDbfs { get; }

    public bool IsVoiced { get; private set; }

    // Feeds one frame's linear RMS and returns the decision for it.
    public bool Push(float rms)
    {
        IsVoiced = IsVoiced ? rms >= _exitRms : rms >= _entryRms;
        return IsVoiced;
    }

    public void Reset() => IsVoiced = false;
}
