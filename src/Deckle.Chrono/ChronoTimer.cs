using System.Diagnostics;

namespace Deckle.Chrono;

// ── ChronoTimer ───────────────────────────────────────────────────────────────
//
// Thin wrapper around System.Diagnostics.Stopwatch for a "time since trigger"
// reading, with no dependency on the HUD visual.
public sealed class ChronoTimer
{
    private readonly Stopwatch _sw = new();

    public bool     IsRunning => _sw.IsRunning;
    public TimeSpan Elapsed   => _sw.Elapsed;

    // Zeroes, then runs (Stopwatch.Restart, not Stopwatch.Start): a new session
    // never inherits the previous one's time.
    public void Start()  => _sw.Restart();

    // Logs only a running timer: a second Stop on an idle clock is not a new
    // reading.
    public void Stop()
    {
        if (!_sw.IsRunning) return;
        _sw.Stop();
        DeckleChronoSource.Log.ClockStopped(_sw.ElapsedMilliseconds);
    }

    // Continues from the current Elapsed without zeroing.
    public void Resume() => _sw.Start();

    public void Reset()  => _sw.Reset();
}
