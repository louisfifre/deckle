using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class EndOfDayTests
{
    private static readonly DateTimeOffset s_evening = Now.AddHours(9);

    [Fact]
    public void EndingTheDayRemovesTreatedRowsAndKeepsTheRowsThatWait()
    {
        var treated = To(minutesAgo: 20);
        var waiting = To(minutesAgo: 10);
        var state = Sorted(
            (treated, MailJudgment.Asks("Facture Infomaniak de novembre disponible")),
            (waiting, MailJudgment.Asks("Envoyer l'attestation demandée par l'assureur")));
        state = state.Trash(treated.Id, Now).State;

        state = state.EndDay(s_evening).State;

        Assert.Equal([waiting.Id], Rows(state).Select(row => row.SubjectId));
    }

    [Fact]
    public void EndingTheDayDismissesEveryInformationLine()
    {
        var line = To();
        var state = Sorted((line, MailJudgment.Informs("Coupure d'eau jeudi")));

        var change = state.EndDay(s_evening);

        Assert.Empty(TriageList.InformationLines(change.State, MailScope.All));
        Assert.Contains(change.Intents, intent => intent is MoveToTrash trash && trash.MailIds.Contains(line.Id));
    }

    [Fact]
    public void SetAsideMailGoesToTheTrashOnlyWhenTheDayEnds()
    {
        var newsletter = To();
        var sorted = TriageState.Empty.Sort(newsletter, MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de novembre"), Now);
        Assert.Empty(sorted.Intents);

        var change = sorted.State.EndDay(s_evening);

        Assert.Equal([newsletter.Id], Assert.IsType<MoveToTrash>(Assert.Single(change.Intents)).MailIds);
        Assert.Equal(0, TriageList.SetAsideCount(change.State));
    }

    [Fact]
    public void TheSummaryOfTheDayCountsTheGesturesOfToday()
    {
        var state = Sorted(
            (To(minutesAgo: 60), MailJudgment.Asks("A")),
            (To(minutesAgo: 50), MailJudgment.Asks("B")),
            (To(minutesAgo: 40), MailJudgment.Asks("C")),
            (To(minutesAgo: 30), MailJudgment.Asks("D")),
            (To(minutesAgo: 20), MailJudgment.Informs("E")),
            (To(minutesAgo: 10), MailJudgment.Informs("F")));
        var rows = Rows(state);
        state = state.AddTask(rows[0].SubjectId, Today, Now).State;
        state = state.AddTask(rows[1].SubjectId, Today, Now).State;
        state = state.Trash(rows[2].SubjectId, Now).State;
        state = state.Trash(rows[3].SubjectId, Now).State;
        state = state.DismissAll(MailScope.All, Now).State;

        Assert.Equal("Today · 2 tasks · 2 moved to trash · 2 dismissed", MailWording.Summary(DaySummary.For(state, s_evening)));
    }

    [Fact]
    public void AnUndoneGestureLeavesTheSummaryOfTheDay()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Asks("A")));
        state = state.Trash(mail.Id, Now).State;
        state = state.UndoLastGesture(Now).State;

        Assert.Equal(new DaySummary(0, 0, 0), DaySummary.For(state, s_evening));
    }

    [Fact]
    public void YesterdaysGesturesLeaveTheSummaryOfTheDay()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Asks("A")));
        state = state.Trash(mail.Id, Now).State;

        Assert.Equal(new DaySummary(0, 0, 0), DaySummary.For(state, Now.AddDays(1)));
    }
}
