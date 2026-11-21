using System.Globalization;

namespace Deckle.Folders;

// When an analysis ran, as the sizes tool words it on Refresh's tooltip and on
// its status line (12.21): the time when it ran today, the date otherwise, the
// way File Explorer's details pane writes a moment. IsToday lets the caller
// pick the wording that goes with it ("at 2:32 PM", "on 11/12/2026").
//
// Both moments are local clock times: the caller converts, so the rule reads
// the same on every machine whatever its time zone.
public readonly record struct SizeAnalysisMoment(bool IsToday, string Text)
{
    public static SizeAnalysisMoment Of(DateTime ranLocal, DateTime nowLocal, CultureInfo culture) =>
        ranLocal.Date == nowLocal.Date
            ? new SizeAnalysisMoment(true, ranLocal.ToString(culture.DateTimeFormat.ShortTimePattern, culture))
            : new SizeAnalysisMoment(false, EntryFormat.Date(ranLocal, culture));
}
