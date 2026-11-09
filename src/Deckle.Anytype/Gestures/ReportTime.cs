using System.Globalization;
using System.Text.RegularExpressions;

namespace Deckle.Anytype;

// The real time of a report, by the rule of the model v6 (section Report):
// the sum of the gaps between two entries that follow each other, except the
// gap that starts at an entry ending with the fixed mark « en attente de
// retour ». The night and the waits on Louis therefore do not count, provided
// the session wrote the mark when it handed over.
//
// Why gaps and not first-to-last: a report follows its session, and a session
// sometimes spans two days; the first-to-last rule counted 28 h 36 on the
// report of 2026-11-05 (measured during the grill of 2026-11-09).
//
// The figure is written into the closing entry itself (« Clôture · temps réel
// 1,5 h. … »), and a task's real time is summed from those entries, never
// incremented. So a report counts once whatever happens: closing it again
// writes a later closing entry that replaces the figure, and the old reports,
// closed before this rule, count nothing rather than a figure they never had.
//
// Pure and I/O-free, so the rule is tested on its own.
public static class ReportTime
{
    public const string AwaitingMark = "en attente de retour";
    public const string ClosingPrefix = "Clôture · temps réel ";

    public readonly record struct Entry(DateTime Stamp, string Text);

    // The stamped entries of a body, oldest first. An entry written before the
    // stamped form has no time and plays no part in the sum.
    public static IReadOnlyList<Entry> Entries(string body)
    {
        var entries = new List<Entry>();
        int stampLength = MarkdownBody.JournalStampFormat.Length;
        foreach (string entry in MarkdownBody.JournalEntries(body))
        {
            if (entry.Length < stampLength
                || !DateTime.TryParseExact(entry[..stampLength], MarkdownBody.JournalStampFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime stamp))
                continue;
            string text = entry[stampLength..].TrimStart();
            if (text.StartsWith('·')) text = text[1..].TrimStart();
            entries.Add(new Entry(stamp, text));
        }
        return entries;
    }

    // The mark ends the entry, give or take a closing period or ellipsis.
    public static bool IsAwaiting(string text) =>
        text.TrimEnd().TrimEnd('.', '…', ' ').EndsWith(AwaitingMark, StringComparison.OrdinalIgnoreCase);

    // A gap that runs backwards (a clock set back, entries out of order) adds
    // nothing rather than taking time away.
    public static TimeSpan Duration(IReadOnlyList<Entry> entries)
    {
        TimeSpan total = TimeSpan.Zero;
        for (int i = 0; i + 1 < entries.Count; i++)
        {
            if (IsAwaiting(entries[i].Text)) continue;
            TimeSpan gap = entries[i + 1].Stamp - entries[i].Stamp;
            if (gap > TimeSpan.Zero) total += gap;
        }
        return total;
    }

    // Hours to two decimals, the precision a journal stamp to the minute allows.
    public static double Hours(TimeSpan duration) => Math.Round(duration.TotalHours, 2);

    public static string Format(double hours) =>
        hours.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR"));

    public static string ClosingLine(double hours, string summary, bool corrected) =>
        $"{ClosingPrefix}{Format(hours)} h{(corrected ? " (corrigé)" : "")}. {summary.Trim()}";

    static readonly Regex ClosingFigure = new(
        "^" + Regex.Escape(ClosingPrefix) + @"(\d+(?:[.,]\d+)?) h", RegexOptions.CultureInvariant);

    // The figure of the last closing entry, or null for a report never closed
    // by this rule.
    public static double? RecordedHours(IReadOnlyList<Entry> entries)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Match m = ClosingFigure.Match(entries[i].Text);
            if (m.Success)
                return double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        }
        return null;
    }
}
