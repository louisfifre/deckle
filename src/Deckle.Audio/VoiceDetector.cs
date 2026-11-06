namespace Deckle.Audio;

// ── VoiceDetector ─────────────────────────────────────────────────────────────
//
// The one energy answer to "is this 50 ms frame voice?". Fed the raw linear
// RMS of each frame, it says voiced or silent, against two levels: one to
// open voice from silence (the entry), one to stay in it (the exit).
//
// The user's voice level (CaptureSettings.VoiceLevelDbfs) is the EXIT level:
// a voiced run ends when a frame falls below it. The ENTRY level sits
// entryMarginDb above it. With the default margin of 0 the two coincide and
// the detector is the plain level test that existed before it.
//
// The cut behaviour from before the detector (everything below the level is
// silence, everything at or above it is voice once speech has started) is
// kept only when the consumer that is still inside speech it has not closed
// asks Resume, not Push: Push from a silent state applies the entry level,
// which would make a frame between the two levels count as pause. That is why
// the segmenter calls Resume in its Hangover; the low-audio warning of the
// capture loop keeps Push, since it looks for an opening of voice.
//
// One detector per consumer (the segmenter, the low-audio warning of the
// capture loop), each fed the same RMS sequence. Deterministic,
// allocation-free, no logging. Single-threaded by construction: Push, Resume
// and Reset run on the one thread that owns the instance, so there is no lock.
public sealed class VoiceDetector
{
    // 0 keeps the opening at the level itself. A positive value makes the
    // opening of a voiced run stricter, and nothing else: leaving voice, and
    // resuming it through Resume, stay at the level. Any positive value is to
    // be sized by replaying the audio corpus (frames that change side,
    // utterances opened differently) before it ships.
    public const double DefaultEntryMarginDb = 0.0;

    private readonly float _exitRms;
    private readonly float _entryRms;

    public VoiceDetector(double voiceLevelDbfs, double entryMarginDb = DefaultEntryMarginDb)
    {
        ExitDbfs  = voiceLevelDbfs;
        EntryDbfs = voiceLevelDbfs + entryMarginDb;

        // dBFS → linear once, so the per-frame test is a plain comparison.
        _exitRms  = (float)Math.Pow(10.0, ExitDbfs / 20.0);
        _entryRms = (float)Math.Pow(10.0, EntryDbfs / 20.0);
    }

    // The level below which a voiced run ends: the user's voice level.
    public double ExitDbfs { get; }

    // The level a silent frame must reach to open a voiced run.
    public double EntryDbfs { get; }

    public bool IsVoiced { get; private set; }

    // Feeds one frame's linear RMS and returns the decision for it: the entry
    // level from silence, the exit level from voice.
    public bool Push(float rms)
    {
        IsVoiced = IsVoiced ? rms >= _exitRms : rms >= _entryRms;
        return IsVoiced;
    }

    // The test a consumer applies while it is still inside speech it has not
    // closed (the segmenter's Hangover): the exit level, never the entry.
    public bool Resume(float rms)
    {
        IsVoiced = rms >= _exitRms;
        return IsVoiced;
    }

    public void Reset() => IsVoiced = false;
}
