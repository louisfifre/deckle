namespace Deckle.Chrono;

// ── ChronoFormatter ───────────────────────────────────────────────────────────
//
// `capSeconds` clamps the displayed elapsed to a hard ceiling, so a display
// that mirrors a recording cap stops advancing at the cap. 0 = no cap.
//
// Minutes wrap at 100 so a session longer than 99 minutes rolls back to 00
// rather than overflowing the two-digit slot.
public static class ChronoFormatter
{
    public readonly record struct Decomposed(int Minutes, int Seconds, int Centiseconds);

    public static Decomposed Decompose(TimeSpan elapsed, int capSeconds = 0)
    {
        if (capSeconds > 0 && elapsed.TotalSeconds > capSeconds)
            elapsed = TimeSpan.FromSeconds(capSeconds);

        int totalMin = (int)elapsed.TotalMinutes;
        return new Decomposed(
            Minutes:      totalMin % 100,
            Seconds:      elapsed.Seconds,
            Centiseconds: elapsed.Milliseconds / 10);
    }

    public static string FormatMmSsCs(TimeSpan elapsed, int capSeconds = 0)
    {
        var d = Decompose(elapsed, capSeconds);
        return $"{d.Minutes:D2}:{d.Seconds:D2}.{d.Centiseconds:D2}";
    }
}
