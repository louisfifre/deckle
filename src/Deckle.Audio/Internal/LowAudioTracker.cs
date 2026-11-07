namespace Deckle.Audio.Internal;

// The live low-audio tracker of a dictation take. The question is "did anything
// cross the voice level for 200 ms in the first 5 s?". The warning fires once
// per recording if the answer is no, which is true both for a broken, muted or
// distant microphone and for a voice level set too high: in either case the
// segmenter would cut nothing.
//
// Why not just count silent duration: a short peak (finger snap, breath hit)
// crosses the level for 50-100 ms, which would reset a naive consecutive
// counter and hide a genuinely broken mic. Instead the positive case is
// tracked: a stretch of at least 200 ms the voice detector calls voiced is
// strong evidence of real speech (one full syllable), and the warning is then
// locked off for the rest of the recording. Peaks are too short to clear 200 ms
// consecutively, so they cannot fake a pass.
//
// The detector is the segmenter's own kind, on the same level
// (host.VoiceLevelDbfs) and the same per-window RMS, so "voice" means the same
// thing here and in the cut. It asks Push, not Resume: the question here is
// whether voice opens, which is what Push answers.
internal sealed class LowAudioTracker(double voiceLevelDbfs)
{
    public const int NormalVoiceSustainedMs = 200;
    public const int WarnAfterSilenceMs = 5000;

    private readonly VoiceDetector _voice = new(voiceLevelDbfs);
    private int _healthyVoiceConsecutiveMs;
    private bool _userVoiceConfirmed;
    private bool _warned;

    public int RecordingMs { get; private set; }

    // The voice level the warning reads, for the log line.
    public double VoiceLevelDbfs => _voice.ExitDbfs;

    // Feeds one window. True exactly once: when the first 5 s ended without a
    // sustained voiced stretch.
    public bool Observe(float rms, int windowMs)
    {
        RecordingMs += windowMs;

        if (!_userVoiceConfirmed)
        {
            if (_voice.Push(rms))
            {
                _healthyVoiceConsecutiveMs += windowMs;
                if (_healthyVoiceConsecutiveMs >= NormalVoiceSustainedMs)
                    _userVoiceConfirmed = true;
            }
            else
            {
                _healthyVoiceConsecutiveMs = 0;
            }
        }

        if (_warned || _userVoiceConfirmed || RecordingMs < WarnAfterSilenceMs)
            return false;

        _warned = true;
        return true;
    }
}
