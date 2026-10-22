namespace Deckle.Audio;

// Watches one capture run for a microphone that opens and starts but never
// hands back audio: waveIn then reports success on every call while the take
// stays empty, and nothing else in the loop notices. Like
// CaptureAnomalyEpisode, the first stall opens the only warning of the run and
// the first audio after it closes it; later stalls only count toward the
// terminal detail.
internal struct CaptureDataWatchdog
{
    // waveIn returns a 50 ms buffer about every 50 ms once started. A full
    // second without audio is twenty missed buffers, far past scheduling
    // jitter, and short enough to show up while the user is still speaking.
    public const int ThresholdMs = 1000;

    private long _lastDataMs;
    private bool _stalled;
    private bool _opened;
    private bool _open;

    public int Stalls { get; private set; }
    public long LongestGapMs { get; private set; }
    public bool Recovered { get; private set; }

    public readonly long MsSinceData(long elapsedMs) => elapsedMs - _lastDataMs;

    // Called on a loop iteration that received audio. elapsedMs counts from
    // the capture start.
    public CaptureAnomalyTransition ObserveData(long elapsedMs)
    {
        TrackGap(elapsedMs);
        _lastDataMs = elapsedMs;
        _stalled = false;

        if (!_open) return CaptureAnomalyTransition.None;
        _open = false;
        Recovered = true;
        return CaptureAnomalyTransition.Recovered;
    }

    // Called on a loop iteration that received no audio, including an
    // iteration whose wait timed out with no buffer done at all.
    public CaptureAnomalyTransition ObserveIdle(long elapsedMs)
    {
        if (_stalled || MsSinceData(elapsedMs) < ThresholdMs)
            return CaptureAnomalyTransition.None;

        _stalled = true;
        Stalls++;
        if (_opened) return CaptureAnomalyTransition.None;

        _opened = true;
        _open = true;
        return CaptureAnomalyTransition.Opened;
    }

    // Folds a stall still running at stop into LongestGapMs.
    public void Close(long elapsedMs)
    {
        if (_stalled) TrackGap(elapsedMs);
    }

    private void TrackGap(long elapsedMs)
    {
        long gap = MsSinceData(elapsedMs);
        if (gap > LongestGapMs) LongestGapMs = gap;
    }
}
