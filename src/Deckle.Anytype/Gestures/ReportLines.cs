using System.Text.RegularExpressions;

namespace Deckle.Anytype;

// How a report is summed up on one line wherever a read lists reports
// (project_overview, task_reports, orchestration_overview): its date, and the
// journal line that says where the work stopped. Pure and I/O-free, so every
// read shows the same thing and the rules are tested once.
//
// A report has no journal-date property in the model v6: its date is the one
// its « # Journal YYYY-MM-DD » heading carries, the creation date standing in
// when the body has no such heading.
internal static partial class ReportLines
{
    // Length of a journal line shown beside a report; report_journal reads it whole.
    public const int LineChars = 120;

    // The closing entry opens with this word (ReportTime.ClosingPrefix).
    const string ClosingWord = "Clôture";

    [GeneratedRegex(@"Journal (\d{4}-\d{2}-\d{2})")]
    private static partial Regex JournalDay();

    // The day of the body's heading, or null when it has none.
    public static string? Day(string markdown) =>
        JournalDay().Match(markdown) is { Success: true } m ? m.Groups[1].Value : null;

    // The first line of the latest journal entry, stamp included, clipped. The
    // « # Journal » heading would only repeat the date, so it is not an entry.
    public static string? LastEntry(string markdown) =>
        MarkdownBody.JournalEntries(markdown).LastOrDefault()?.Split('\n')[0] is string line
            ? Clip(line)
            : null;

    // The first line of the latest closing entry, without its stamp, clipped;
    // null for a report not closed (or closed before the stamped form).
    public static string? ClosingEntry(string markdown)
    {
        foreach (ReportTime.Entry entry in ReportTime.Entries(markdown).Reverse())
            if (entry.Text.StartsWith(ClosingWord, StringComparison.Ordinal))
                return Clip(entry.Text.Split('\n')[0]);
        return null;
    }

    public static string Clip(string line) =>
        line.Length <= LineChars ? line : line[..LineChars] + "…";
}
