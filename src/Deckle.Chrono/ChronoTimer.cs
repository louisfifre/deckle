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

    public void Stop()   => _sw.Stop();

    // Continues from the current Elapsed without zeroing.
    public void Resume() => _sw.Start();

    public void Reset()  => _sw.Reset();
}
