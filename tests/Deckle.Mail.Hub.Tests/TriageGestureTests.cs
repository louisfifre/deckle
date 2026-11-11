using Xunit;

namespace Deckle.Mail.Hub.Tests;

// What the person notices of Triage: the order of the list, a gesture that
// greys its row in place, its undo, the cross, the end of the day, and the
// gestures that cannot happen.
[Trait("Category", "unit")]
public sealed class TriageGestureTests
{
    private static (TriageSession Session, SampleMailHubSource Source, ManualClock Clock) Open(
        SampleScenario scenario = SampleScenario.NormalDay)
    {
        var source = new SampleMailHubSource(scenario);
        var clock = new ManualClock();
        return (new TriageSession(source, clock), source, clock);
    }

    [Fact]
    public void TheListPutsDeadlinesFirstThenTheOldest()
    {
        (TriageSession session, _, _) = Open();

        Assert.Equal(
            ["s-drive", "s-france-travail", "s-camille", "s-devis"],
            session.Rows.Select(r => r.Id));
    }

    [Fact]
    public void ATaskGreysItsRowWhereItIsAndWritesAnActivityLine()
    {
        (TriageSession session, SampleMailHubSource source, _) = Open();

        Assert.True(session.AddTask("s-camille", new DateOnly(2026, 11, 10)));

        TriageRow row = session.Rows[2];
        Assert.Equal("s-camille", row.Id);
        Assert.Equal(GestureResult.Task(new DateOnly(2026, 11, 10)), row.Result);
        Assert.Equal("s-camille", session.Activity[0].SubjectId);
        Assert.Equal(new MailGesture(MailGestureKind.AddTask, "s-camille", new DateOnly(2026, 11, 10)), source.Applied.Single());
    }

    [Fact]
    public void UndoOnTheRowGivesItsControlsBackAndRemovesItsLine()
    {
        (TriageSession session, _, ManualClock clock) = Open();
        int lines = session.Activity.Count;
        session.MoveToTrash("s-devis");
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(session.Undo("s-devis"));

        Assert.False(session.FindRow("s-devis")!.IsTreated);
        Assert.Equal(lines, session.Activity.Count);
        Assert.Equal(4, session.Rows.Count);
    }

    [Fact]
    public void CtrlZUndoesTheLastGestureFirst()
    {
        (TriageSession session, _, _) = Open();
        session.MoveToTrash("s-camille");
        session.AddTask("s-drive", session.Today);

        Assert.True(session.UndoLast());

        Assert.False(session.FindRow("s-drive")!.IsTreated);
        Assert.True(session.FindRow("s-camille")!.IsTreated);
    }

    [Fact]
    public void AClickInTheInstantAfterAGestureIsDropped()
    {
        (TriageSession session, _, ManualClock clock) = Open();
        session.AddTask("s-drive", session.Today);

        Assert.False(session.AcceptsClick("s-drive"));
        clock.Advance(TriageSession.ClickGuard);
        Assert.True(session.AcceptsClick("s-drive"));
    }

    // The second click of a double click on Undo lands on the Add task that
    // Undo brings back; it must not open the menu of days.
    [Fact]
    public void AClickInTheInstantAfterUndoIsDropped()
    {
        (TriageSession session, _, ManualClock clock) = Open();
        session.AddTask("s-drive", session.Today);
        clock.Advance(TriageSession.ClickGuard);
        session.Undo("s-drive");

        Assert.False(session.AcceptsClick("s-drive"));
        clock.Advance(TriageSession.ClickGuard);
        Assert.True(session.AcceptsClick("s-drive"));
    }

    [Fact]
    public void TheCrossRemovesOnlyATreatedRow()
    {
        (TriageSession session, _, _) = Open();

        Assert.False(session.RemoveTreatedRow("s-drive"));
        session.MoveToTrash("s-drive");
        Assert.True(session.RemoveTreatedRow("s-drive"));

        Assert.Null(session.FindRow("s-drive"));
        Assert.Equal(3, session.Rows.Count);
    }

    [Fact]
    public void EndingTheDayRemovesTreatedRowsAndEveryLineAndSendsTheSetAsideMails()
    {
        (TriageSession session, _, _) = Open();
        session.MoveToTrash("s-camille");
        Assert.True(session.SetAsideCount(MailFilter.All) > 0);

        session.EndDay();

        Assert.Equal(["s-drive", "s-france-travail", "s-devis"], session.Rows.Select(r => r.Id));
        Assert.Empty(session.InfoLines);
        Assert.Equal(0, session.SetAsideCount(MailFilter.All));
    }

    [Fact]
    public void DoneForTodayIsEmphasisedOnlyWhenEveryRowIsTreated()
    {
        (TriageSession session, _, _) = Open();
        session.MoveToTrash("s-drive");
        session.MoveToTrash("s-france-travail");
        session.MoveToTrash("s-camille");
        Assert.False(session.IsEveryRowTreated(MailFilter.All));

        session.MoveToTrash("s-devis");

        Assert.True(session.IsEveryRowTreated(MailFilter.All));
    }

    [Fact]
    public void DismissAllOnAnInboxPageDismissesOnlyItsLines()
    {
        (TriageSession session, _, _) = Open();

        int dismissed = session.DismissAllInfo(MailFilter.Mailbox("mb-perso"));

        Assert.Equal(2, dismissed);
        Assert.Equal(["i-eau"], session.InfoLines.Select(i => i.Id));
    }

    [Fact]
    public void DismissAllTakesTheSetAsideLineOffThePageAndSendsNothingForIt()
    {
        (TriageSession session, SampleMailHubSource source, _) = Open();
        int setAside = session.SetAsideCount(MailFilter.All);
        Assert.True(setAside > 0);

        int dismissed = session.DismissAllInfo(MailFilter.All);

        Assert.Equal(0, session.SetAsideCount(MailFilter.All));
        // The mails themselves still wait for the end of the day: only the
        // lines were handed to the source.
        Assert.Equal(dismissed, source.Applied.Count);
        Assert.All(source.Applied, g => Assert.Equal(MailGestureKind.DismissInfo, g.Kind));
        Assert.Equal(setAside, session.Activity.Count(a => a.AwaitsEndOfDay));
    }

    [Fact]
    public void WithoutAnytypeNoTaskCanBeAdded()
    {
        (TriageSession session, SampleMailHubSource source, _) = Open(SampleScenario.AnytypeNotConnected);

        Assert.False(session.AddTask("s-drive", session.Today));
        Assert.Empty(source.Applied);
        Assert.Contains(session.Problems, p => p.Kind == MailProblemKind.AnytypeNotConnected);
    }

    [Fact]
    public void AGestureOnAMailboxThatCannotConnectWaitsAndIsCountedInItsProblem()
    {
        (TriageSession session, _, _) = Open(SampleScenario.MailboxCannotConnect);

        session.AddTask("s-devis", session.Today);

        Assert.True(session.WaitsForMailbox(session.FindRow("s-devis")!));
        MailProblem problem = Assert.Single(session.Problems);
        Assert.Equal("mb-maison", problem.MailboxId);
        Assert.Equal(1, problem.WaitingGestures);
    }

    [Fact]
    public void UndoOnALineOfTheSortingBringsItsSubjectToTheEndOfTriage()
    {
        (TriageSession session, SampleMailHubSource source, _) = Open();
        ActivityEntry line = session.Activity.First(a => a.SubjectId == "sa-doctolib");

        Assert.True(session.UndoActivity(line.Id));

        Assert.Equal("sa-doctolib", session.Rows[^1].Id);
        Assert.True(session.FindActivity(line.Id)!.Undone);
        Assert.Equal(MailGestureKind.BringBack, source.Applied.Single().Kind);
    }

    [Fact]
    public void NothingWaitingShowsNoRowButKeepsTheDaysActivity()
    {
        (TriageSession session, _, _) = Open(SampleScenario.NothingWaiting);

        Assert.Empty(session.Rows);
        Assert.Empty(session.InfoLines);
        Assert.True(session.HasActivity);
    }

    [Fact]
    public void RemovingAMailboxTakesItsRowsLinesAndSubjectsWithIt()
    {
        (TriageSession session, _, _) = Open();

        session.RemoveMailbox("mb-maison");

        Assert.Null(session.FindRow("s-devis"));
        Assert.Null(session.FindSubject("s-devis"));
        Assert.DoesNotContain(session.InfoLines, i => i.MailboxId == "mb-maison");
    }
}

// A clock the test moves by hand, for the instant guard.
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
