namespace Deckle.Input;

// Folds a repeating degradation into one incident, so the log gets one
// Warning when it starts and one recovery when it ends instead of a line per
// occurrence. An occurrence streak opens the incident once it reaches the
// threshold; Clear closes it once no occurrence was recorded for quietMs.
// Clearing before the threshold forgets the streak: isolated occurrences
// never open an incident.
//
// Not thread-safe: the keyboard host drives it from its input thread only.
internal sealed class RepeatingIncident(int threshold, double quietMs)
{
    private int _streak;
    private double _lastOccurrenceMs = double.NegativeInfinity;

    public bool IsOpen { get; private set; }

    // Occurrences in the current streak, including those after it opened.
    public int Occurrences => _streak;

    // Returns true when this record opens the incident.
    public bool Record(double nowMs, int occurrences = 1)
    {
        if (occurrences <= 0) return false;

        _streak += occurrences;
        _lastOccurrenceMs = nowMs;
        if (IsOpen || _streak < threshold) return false;

        IsOpen = true;
        return true;
    }

    // Returns true when an open incident closes; total then holds every
    // occurrence it counted. An open incident stays open while its last
    // occurrence is more recent than quietMs.
    public bool Clear(double nowMs, out int total)
    {
        total = 0;
        if (!IsOpen)
        {
            _streak = 0;
            return false;
        }

        if (nowMs - _lastOccurrenceMs < quietMs) return false;

        total = _streak;
        _streak = 0;
        IsOpen = false;
        return true;
    }

    // Closes the incident whatever the quiet period, for a host shutting down.
    public bool Close(out int total)
    {
        total = IsOpen ? _streak : 0;
        bool wasOpen = IsOpen;
        _streak = 0;
        IsOpen = false;
        return wasOpen;
    }
}
