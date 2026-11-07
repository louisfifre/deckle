namespace Deckle.Audio;

// ── RecordingLimit ───────────────────────────────────────────────────────────
//
// The maximum recording length as the Recording page shows it: a switch and a
// length in minutes. The POCO and the capture loop keep seconds,
// with 0 meaning "no limit", so this class is the one place the two meet. It is
// pure (no settings service, no disk) so the rules are tested without a page.
internal static class RecordingLimit
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 120;

    // The length the switch restores when no earlier length is known.
    public const int DefaultMinutes = 30;

    public static bool IsLimited(int seconds) => seconds > 0;

    // The length to show. A stored value outside the slider's range (a
    // hand-edited file) is shown at the nearest end rather than beyond it; the
    // file is left as it is until the person moves the control. While the limit
    // is off, the last length kept stands in, so the hidden field is ready.
    public static int MinutesToShow(int seconds, int rememberedMinutes) =>
        Clamp(seconds > 0 ? (int)Math.Round(seconds / 60.0) : rememberedMinutes);

    public static int SecondsFor(int minutes) => Clamp(minutes) * 60;

    public static int Clamp(int minutes) => Math.Clamp(minutes, MinMinutes, MaxMinutes);
}
