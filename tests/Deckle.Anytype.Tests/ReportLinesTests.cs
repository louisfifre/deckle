using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The one-line summary of a report, shared by every read that lists reports.
[Trait("Category", "unit")]
public class ReportLinesTests
{
    const string Closed =
        "# Journal 2026-11-10\n\n2026-11-10 09:00\n\nDébut\n\n"
        + "2026-11-10 10:30\n\nClôture · temps réel 1,5 h. Tout est fait.\n\nSuite du texte";

    [Fact]
    public void DayIsTheOneOfTheJournalHeadingAndNullWithoutHeading()
    {
        Assert.Equal("2026-11-10", ReportLines.Day(Closed));
        Assert.Null(ReportLines.Day("2026-11-10 09:00\n\nDébut"));
    }

    [Fact]
    public void ClosingEntryIsTheFirstLineOfTheClosingTextWithoutItsStamp()
    {
        Assert.Equal("Clôture · temps réel 1,5 h. Tout est fait.", ReportLines.ClosingEntry(Closed));
    }

    [Fact]
    public void ClosingEntryIsNullForAReportNotClosed()
    {
        Assert.Null(ReportLines.ClosingEntry("# Journal 2026-11-10\n\n2026-11-10 09:00\n\nDébut"));
    }

    [Fact]
    public void LastEntryKeepsItsStampAndIsClippedLikeProjectOverviewClipsIt()
    {
        string longText = new('x', ReportLines.LineChars + 30);
        string last = ReportLines.LastEntry($"# Journal 2026-11-10\n\n2026-11-10 09:00\n\n{longText}")!;

        Assert.StartsWith("2026-11-10 09:00 · xxx", last);
        Assert.Equal(ReportLines.LineChars + 1, last.Length);
        Assert.EndsWith("…", last);
    }
}
