using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class ActivityTests
{
    [Fact]
    public void ActivityIsEmptyUntilThePersonOrTheSortingDoesSomething()
    {
        var state = Sorted((To(), MailJudgment.Asks("Facture Infomaniak de novembre disponible")));
        Assert.True(ActivityLog.IsEmpty(state));

        var setAside = Sorted((To(), MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de novembre")));
        Assert.False(ActivityLog.IsEmpty(setAside));
    }

    [Fact]
    public void WhatThePersonDidAndWhatTheSortingDidAloneAreTwoParts()
    {
        var asks = To(minutesAgo: 20);
        var newsletter = To(minutesAgo: 10);
        var state = Sorted(
            (asks, MailJudgment.Asks("Facture Infomaniak de novembre disponible")),
            (newsletter, MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de novembre")));
        state = state.Trash(asks.Id, Now).State;

        Assert.Equal([asks.Id], ActivityLog.ByPerson(state).SelectMany(day => day.Lines).Select(line => line.SubjectId));
        Assert.Equal([newsletter.Id], ActivityLog.BySorting(state).SelectMany(day => day.Lines).Select(line => line.MailId));
    }

    [Fact]
    public void LinesAreGroupedByDayNewestFirst()
    {
        var monday = To(minutesAgo: 60);
        var tuesdayEarly = To(minutesAgo: 50);
        var tuesdayLate = To(minutesAgo: 40);
        var state = Sorted(
            (monday, MailJudgment.Asks("A")),
            (tuesdayEarly, MailJudgment.Asks("B")),
            (tuesdayLate, MailJudgment.Asks("C")));
        state = state.Trash(monday.Id, Now.AddDays(-1)).State;
        state = state.Trash(tuesdayEarly.Id, Now).State;
        state = state.Trash(tuesdayLate.Id, Now.AddHours(1)).State;

        var days = ActivityLog.ByPerson(state);

        Assert.Equal([Today, Today.AddDays(-1)], days.Select(day => day.Day));
        Assert.Equal([tuesdayLate.Id, tuesdayEarly.Id], days[0].Lines.Select(line => line.SubjectId));
    }

    [Fact]
    public void ALineStaysOnTheDayItWasMadeAfterTheClockChanges()
    {
        // 00:30 on 20 October in summer time is 23:30 on 19 October in winter time.
        var summerNight = new DateTimeOffset(2026, 10, 20, 0, 30, 0, TimeSpan.FromHours(2));
        var mail = To();
        var state = Sorted((mail, MailJudgment.Asks("A")));
        state = state.Trash(mail.Id, summerNight).State;

        var day = Assert.Single(ActivityLog.ByPerson(state));

        Assert.Equal(new DateOnly(2026, 10, 20), day.Day);
    }

    [Fact]
    public void EachSetAsideMailHasItsOwnLineEvenFromOneSender()
    {
        var state = Sorted(
            (To(from: "lettre@example.com", minutesAgo: 20), MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de novembre")),
            (To(from: "lettre@example.com", minutesAgo: 10), MailJudgment.SetAside(SetAsideReason.Promotion, "Soldes de novembre")));

        var lines = Assert.Single(ActivityLog.BySorting(state)).Lines;
        Assert.Equal([SetAsideReason.Promotion, SetAsideReason.Newsletter], lines.Select(line => line.Reason));
    }

    [Fact]
    public void AnUndoneLineStaysAndSaysItsSubjectIsBackInTriage()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Asks("Facture Infomaniak de novembre disponible")));
        state = state.Trash(mail.Id, Now).State;

        state = state.UndoLastGesture(Now).State;

        var line = Assert.Single(Assert.Single(ActivityLog.ByPerson(state)).Lines);
        Assert.True(line.BackInTriage);
        Assert.Single(Rows(state));
    }

    [Fact]
    public void UndoOnALineOfThePersonBringsAClosedSubjectBackToTriage()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Asks("Facture Infomaniak de novembre disponible")));
        state = state.Trash(mail.Id, Now).State;
        state = state.RemoveTreatedRows().State;
        Assert.Empty(Rows(state));

        var change = state.Undo(mail.Id, Now.AddMinutes(5));

        Assert.Single(Rows(change.State));
        Assert.Contains(change.Intents, intent => intent is RestoreFromTrash);
    }

    [Fact]
    public void UndoOnASortingLineBringsTheMailBackToTriageAsASubject()
    {
        var newsletter = To(from: "lettre@example.com");
        var state = Sorted((newsletter, MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de novembre")));

        state = state.UndoSorting(newsletter.Id, Now).State;

        Assert.Equal("Lettre de novembre", Assert.Single(Rows(state)).Sentence);
        Assert.Equal(0, TriageList.SetAsideCount(state));
        Assert.True(Assert.Single(Assert.Single(ActivityLog.BySorting(state)).Lines).BackInTriage);
    }

    [Fact]
    public void UndoOnASortingLineAfterTheDayEndedBringsTheMailBackFromTheTrash()
    {
        var newsletter = To(from: "lettre@example.com");
        var state = Sorted((newsletter, MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de novembre")));
        state = state.EndDay(Now.AddHours(9)).State;

        var change = state.UndoSorting(newsletter.Id, Now.AddDays(1));

        Assert.Equal([newsletter.Id], Assert.IsType<RestoreFromTrash>(Assert.Single(change.Intents)).MailIds);
        Assert.Single(Rows(change.State));
    }

    [Fact]
    public void UndoOnASortingLineCorrectsTheNextMailsOfThatKindFromThatSender()
    {
        var first = To(from: "lettre@example.com", minutesAgo: 60);
        var state = Sorted((first, MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de novembre")));
        state = state.UndoSorting(first.Id, Now).State;

        state = state.Sort(To(from: "LETTRE@example.com"), MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de décembre"), Now).State;
        state = state.Sort(To(from: "lettre@example.com"), MailJudgment.SetAside(SetAsideReason.Promotion, "Soldes de décembre"), Now).State;
        state = state.Sort(To(from: "autre@example.com"), MailJudgment.SetAside(SetAsideReason.Newsletter, "Autre lettre"), Now).State;

        Assert.Contains(Rows(state), row => row.Sentence == "Lettre de décembre");
        Assert.Equal(2, TriageList.SetAsideCount(state));
    }

    [Fact]
    public void UndoOnASortingLineBringsTheSubjectBackWhateverItBecameSince()
    {
        // Set aside, then turned into an information line by a later mail, then dismissed and trashed.
        var onItsWay = To(minutesAgo: 600);
        var delivered = To(minutesAgo: 60) with { InReplyTo = onItsWay.Id };
        var state = Sorted(
            (onItsWay, MailJudgment.SetAside(SetAsideReason.NoActionNeeded, "Colis en route")),
            (delivered, MailJudgment.Informs("Colis livré")));
        state = state.Dismiss(onItsWay.Id, Now).State;

        var change = state.UndoSorting(onItsWay.Id, Now.AddMinutes(5));

        Assert.Equal(onItsWay.Id, Assert.Single(Rows(change.State)).SubjectId);
        Assert.Equal([onItsWay.Id, delivered.Id], Assert.IsType<RestoreFromTrash>(Assert.Single(change.Intents)).MailIds);
    }

    [Fact]
    public void TheSortingLinesOfOneSubjectAllSayItIsBackInTriage()
    {
        var onItsWay = To(minutesAgo: 600);
        var stillOnItsWay = To(minutesAgo: 60) with { InReplyTo = onItsWay.Id };
        var state = Sorted(
            (onItsWay, MailJudgment.SetAside(SetAsideReason.NoActionNeeded, "Colis en route")),
            (stillOnItsWay, MailJudgment.SetAside(SetAsideReason.NoActionNeeded, "Colis toujours en route")));

        state = state.UndoSorting(onItsWay.Id, Now).State;

        Assert.Single(Rows(state));
        Assert.All(Assert.Single(ActivityLog.BySorting(state)).Lines, line => Assert.True(line.BackInTriage));
    }
}
