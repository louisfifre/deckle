namespace Deckle.Hud;

// Splits the clock's elapsed time into the minutes, seconds and centiseconds
// the six glyphs show.
//
// `capSeconds` clamps the displayed elapsed to a hard ceiling, so a display
// that mirrors a recording cap stops advancing at the cap. 0 = no cap.
//
// Minutes wrap at 100 so a session longer than 99 minutes rolls back to 00
// rather than overflowing the two-digit slot.
internal static class ChronoDigits
{
    internal readonly record struct Decomposed(int Minutes, int Seconds, int Centiseconds);

    internal static Decomposed Decompose(TimeSpan elapsed, int capSeconds = 0)
    {
        if (capSeconds > 0 && elapsed.TotalSeconds > capSeconds)
            elapsed = TimeSpan.FromSeconds(capSeconds);

        int totalMin = (int)elapsed.TotalMinutes;
        return new Decomposed(
            Minutes:      totalMin % 100,
            Seconds:      elapsed.Seconds,
            Centiseconds: elapsed.Milliseconds / 10);
    }
}
