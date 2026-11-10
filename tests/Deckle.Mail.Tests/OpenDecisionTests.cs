using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

/// <summary>
/// Today's behaviour on questions the maintainer has not decided. Each test
/// pins what the code does now, so that whichever way he decides, the test of
/// that question turns red at the right place and is rewritten to his word.
/// </summary>
[Trait("Category", "unit")]
public class OpenDecisionTests
{
    [Fact]
    public void AnUnverifiedReplyJoinsItsThreadAndMakesTheRowTrashOnly()
    {
        // Open decision: whether a mail whose sender failed the checks may join an existing subject.
        var request = To(minutesAgo: 600);
        var spoofed = To(minutesAgo: 60) with { InReplyTo = request.Id, SenderVerified = false };
        var state = Sorted(
            (request, MailJudgment.Asks("Envoyer l'attestation demandée par l'assureur")),
            (spoofed, MailJudgment.Asks("Payer les frais de dossier")));

        var row = Assert.Single(Rows(state));
        Assert.False(row.CanAddTask);
    }

    [Fact]
    public void AJoiningMailThatStatesNoDeadlineClearsTheSubjectsDeadline()
    {
        // Open decision: whether a later mail without a deadline replaces the one the subject had.
        var request = To(minutesAgo: 600);
        var reply = To(minutesAgo: 60) with { InReplyTo = request.Id };
        var state = Sorted(
            (request, MailJudgment.Asks("Envoyer l'attestation avant le 20", On(11, 20))),
            (reply, MailJudgment.Asks("Envoyer aussi le RIB")));

        Assert.Null(Assert.Single(Rows(state)).ProposedDay);
    }

    [Fact]
    public void TheSameMessageIdInTwoMailboxesIsSortedOnce()
    {
        // Open decision: whether one mail delivered to two of the person's mailboxes is one subject or two.
        var inPerso = To(id: "<shared@example.com>");
        var inMaison = To(deliveredTo: "maison@exemple.fr", mailboxId: "maison", id: "<shared@example.com>");
        var state = Sorted((inPerso, MailJudgment.Asks("Réunion de copropriété jeudi")));

        Assert.True(state.Sort(inMaison, MailJudgment.Asks("Réunion de copropriété jeudi"), Now).IsUnchanged);
        Assert.Empty(Rows(state, MailScope.Mailbox("maison")));
    }

    [Fact]
    public void UndoingATreatedSubjectAfterAReplyStartedItsOwnRowLeavesTwoRowsForOneThread()
    {
        // Open decision: whether an undone subject and the row its reply started become one subject again.
        var request = To(minutesAgo: 600);
        var reply = To(minutesAgo: 60) with { InReplyTo = request.Id };
        var state = Sorted((request, MailJudgment.Asks("Envoyer l'attestation demandée par l'assureur")));
        state = state.Trash(request.Id, Now).State;
        state = state.Sort(reply, MailJudgment.Asks("Attestation toujours attendue"), Now).State;

        state = state.Undo(request.Id, Now.AddMinutes(5)).State;

        Assert.Equal(2, Rows(state).Count(row => row.Result is null));
    }

    [Fact]
    public void CtrlZAfterTheEndOfTheDayDoesNotBringSetAsideMailBack()
    {
        // Open decision: whether Ctrl+Z after the end of the day restores the set-aside mail it trashed.
        var newsletter = To();
        var state = Sorted((newsletter, MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de novembre")));
        state = state.EndDay(Now.AddHours(9)).State;

        var change = state.UndoLastGesture(Now.AddHours(9));

        Assert.DoesNotContain(change.Intents, intent => intent is RestoreFromTrash);
        Assert.Equal(0, TriageList.SetAsideCount(change.State));
    }

    [Fact]
    public void AVerifiedReplyLiftsTheUnverifiedLabelOfItsSubject()
    {
        // Open decision: whether « Sender not verified » is read from the latest mail only or from any mail of the subject.
        var unverified = To(minutesAgo: 600) with { SenderVerified = false };
        var verifiedReply = To(minutesAgo: 60) with { InReplyTo = unverified.Id };
        var state = Sorted(
            (unverified, MailJudgment.Asks("Confirmer le virement")),
            (verifiedReply, MailJudgment.Asks("Confirmer le virement avant vendredi")));

        var row = Assert.Single(Rows(state));
        Assert.True(row.SenderVerified);
        Assert.True(row.CanAddTask);
    }

    [Fact]
    public void TodayReadsAsAWordInAResultAndAsADateOnTheTaskControl()
    {
        // Open decision: whether the task control also says « today » for a day the result calls « today ».
        Assert.Equal("Task for today", MailWording.Result(new TreatmentResult(TreatmentKind.Task, Today, Waiting: false), Today));
        Assert.Equal("Add task · Tue 10", MailWording.AddTask(Today, Today));
    }
}
