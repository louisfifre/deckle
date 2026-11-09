using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The real-time rule of the model v6 on its own: gaps between entries, minus
// the gaps that start at an entry waiting on the person.
[Trait("Category", "unit")]
public class ReportTimeTests
{
    static ReportTime.Entry At(int day, int hour, int minute, string text) =>
        new(new DateTime(2026, 11, day, hour, minute, 0), text);

    [Fact]
    public void DurationSumsTheGapsAndSkipsTheOnesAfterAnEntryWaitingOnThePerson()
    {
        TimeSpan duration = ReportTime.Duration(
        [
            At(9, 9, 0, "Début"),
            At(9, 10, 0, "Livré, en attente de retour."),
            At(9, 13, 0, "Retour de Louis lu"),
            At(9, 14, 5, "Clôture"),
        ]);

        Assert.Equal(TimeSpan.FromMinutes(60 + 65), duration);
    }

    // The case that made the rule: a session left open overnight.
    [Fact]
    public void ANightAfterAnEntryWaitingOnThePersonDoesNotCount()
    {
        TimeSpan duration = ReportTime.Duration(
        [
            At(5, 13, 48, "Ouverture"),
            At(5, 18, 0, "Fin de journée, en attente de retour"),
            At(6, 17, 0, "Reprise"),
            At(6, 18, 24, "Clôture"),
        ]);

        Assert.Equal(TimeSpan.FromMinutes(252 + 84), duration);
    }

    [Fact]
    public void AGapRunningBackwardsAddsNothing()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), ReportTime.Duration(
        [
            At(9, 10, 0, "a"),
            At(9, 9, 0, "b"),
            At(9, 9, 30, "c"),
        ]));
    }

    [Fact]
    public void EntriesAreReadFromTheJournalBodyWithTheirStamps()
    {
        IReadOnlyList<ReportTime.Entry> entries = ReportTime.Entries(
            "# Journal 2026-11-09\n\n- une puce d'avant les horodatages\n\n"
            + "2026-11-09 09:00\n\nDébut\n\n --- \n\n2026-11-09 10:15\n\nSuite\nsur deux lignes");

        Assert.Equal(2, entries.Count);
        Assert.Equal(new DateTime(2026, 11, 9, 9, 0, 0), entries[0].Stamp);
        Assert.Equal("Début", entries[0].Text);
        Assert.Equal("Suite\nsur deux lignes", entries[1].Text);
    }

    [Fact]
    public void TheRecordedFigureIsTheOneOfTheLastClosingEntry()
    {
        double? hours = ReportTime.RecordedHours(
        [
            At(9, 10, 0, ReportTime.ClosingLine(1.5, "Première clôture", corrected: false)),
            At(9, 11, 0, "Reprise"),
            At(9, 12, 0, ReportTime.ClosingLine(2, "Seconde", corrected: true)),
        ]);

        Assert.Equal(2, hours);
    }

    [Fact]
    public void AReportClosedBeforeTheRuleRecordsNothing()
    {
        Assert.Null(ReportTime.RecordedHours([At(7, 19, 16, "Clôture : code, tests et merge faits")]));
    }

    [Fact]
    public void TheClosingLineWritesHoursTheFrenchWayAndReadsThemBack()
    {
        string line = ReportTime.ClosingLine(2.08, "Fini.", corrected: false);

        Assert.Equal("Clôture · temps réel 2,08 h. Fini.", line);
        Assert.Equal(2.08, ReportTime.RecordedHours([At(9, 9, 0, line)]));
    }
}
