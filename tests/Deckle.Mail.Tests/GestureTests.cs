using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class GestureTests
{
    private static readonly DateTimeOffset s_later = Now.AddSeconds(5);

    private static (TriageState State, Mail Mail) OneRow()
    {
        var mail = To(from: "facturation@example.com");
        return (Sorted((mail, MailJudgment.Asks("Facture Infomaniak de novembre disponible", On(11, 15)))), mail);
    }

    [Fact]
    public void AddingATaskCreatesItAndSendsTheSubjectsMailsToTheTrash()
    {
        var (state, mail) = OneRow();

        var change = state.AddTask(mail.Id, new DateOnly(2026, 11, 13), Now);

        var task = Assert.IsType<CreateTask>(change.Intents[0]);
        Assert.Equal("Facture Infomaniak de novembre disponible", task.Sentence);
        Assert.Equal("facturation@example.com", task.Sender.Address);
        Assert.Equal(mail.Id, task.MailId);
        Assert.Equal(new DateOnly(2026, 11, 13), task.Day);
        var trash = Assert.IsType<MoveToTrash>(change.Intents[1]);
        Assert.Equal([mail.Id], trash.MailIds);
        Assert.Equal("perso", trash.MailboxId);
    }

    [Fact]
    public void TheTaskCarriesTheMailWhoseSentenceTheRowShows()
    {
        // A reminder that only repeats the request: the row keeps the request's sentence, so the task keeps its mail.
        var request = To(from: "assureur@example.com", minutesAgo: 600);
        var reminder = To(from: "relance@example.com", minutesAgo: 60) with { InReplyTo = request.Id };
        var state = Sorted(
            (request, MailJudgment.Asks("Envoyer l'attestation demandée par l'assureur")),
            (reminder, MailJudgment.RepeatsEarlierRequest("Relance : attestation")));

        var task = Assert.IsType<CreateTask>(state.AddTask(request.Id, Today, Now).Intents[0]);

        Assert.Equal("Envoyer l'attestation demandée par l'assureur", task.Sentence);
        Assert.Equal(request.Id, task.MailId);
        Assert.Equal("assureur@example.com", task.Sender.Address);
    }

    [Fact]
    public void ATreatedRowGreysInPlaceWithItsResult()
    {
        var (state, mail) = OneRow();

        state = state.AddTask(mail.Id, new DateOnly(2026, 11, 13), Now).State;

        var row = Assert.Single(Rows(state));
        Assert.Equal(new TreatmentResult(TreatmentKind.Task, new DateOnly(2026, 11, 13), Waiting: false), row.Result);
        Assert.Equal("Task for Fri 13", MailWording.Result(row.Result!, Today));
    }

    [Fact]
    public void TheTrashSendsTheSubjectsMailsToTheTrashOfTheirMailbox()
    {
        var first = To(minutesAgo: 90);
        var reply = To(minutesAgo: 30) with { InReplyTo = first.Id };
        var state = Sorted(
            (first, MailJudgment.Asks("Confirmer le rendez-vous")),
            (reply, MailJudgment.Asks("Confirmer le rendez-vous de jeudi")));

        var change = state.Trash(first.Id, Now);

        var trash = Assert.IsType<MoveToTrash>(Assert.Single(change.Intents));
        Assert.Equal([first.Id, reply.Id], trash.MailIds);
        Assert.Equal("Moved to trash", MailWording.Result(Assert.Single(Rows(change.State)).Result!, Today));
    }

    [Fact]
    public void UndoBringsTheMailsBackRemovesTheTaskAndGivesTheRowItsControlsBack()
    {
        var (state, mail) = OneRow();
        state = state.AddTask(mail.Id, Today, Now).State;

        var change = state.Undo(mail.Id, s_later);

        Assert.Contains(change.Intents, intent => intent is RemoveTask remove && remove.SubjectId == mail.Id);
        Assert.Contains(change.Intents, intent => intent is RestoreFromTrash restore && restore.MailIds.Contains(mail.Id));
        var row = Assert.Single(Rows(change.State));
        Assert.Null(row.Result);
        Assert.True(row.CanAddTask);
    }

    [Fact]
    public void CtrlZUndoesTheLastGesture()
    {
        var first = To(minutesAgo: 20);
        var second = To(minutesAgo: 10);
        var state = Sorted(
            (first, MailJudgment.Asks("Facture Infomaniak de novembre disponible")),
            (second, MailJudgment.Asks("Envoyer l'attestation demandée par l'assureur")));
        state = state.Trash(first.Id, Now).State;
        state = state.Trash(second.Id, Now.AddSeconds(1)).State;

        // Ctrl+Z is not a click: it is not held back by the instant after a gesture.
        state = state.UndoLastGesture(Now.AddSeconds(1)).State;

        Assert.Null(Rows(state).Single(row => row.SubjectId == second.Id).Result);
        Assert.NotNull(Rows(state).Single(row => row.SubjectId == first.Id).Result);
    }

    [Fact]
    public void AClickInTheInstantAfterAGestureDoesNothing()
    {
        var (state, mail) = OneRow();
        state = state.AddTask(mail.Id, Today, Now).State;

        // The second click of a double click on Add task lands where Undo now stands.
        var change = state.Undo(mail.Id, Now + state.Options.ClickGuard / 2);

        Assert.True(change.IsUnchanged);
        Assert.Empty(change.Intents);
        Assert.NotNull(Assert.Single(Rows(change.State)).Result);
    }

    [Fact]
    public void AClickAfterTheInstantIsHeard()
    {
        var (state, mail) = OneRow();
        state = state.AddTask(mail.Id, Today, Now).State;

        Assert.False(state.Undo(mail.Id, Now + state.Options.ClickGuard).IsUnchanged);
    }

    [Fact]
    public void TheCrossOfATreatedRowRemovesIt()
    {
        var (state, mail) = OneRow();
        state = state.Trash(mail.Id, Now).State;

        state = state.RemoveRow(mail.Id, s_later).State;

        Assert.Empty(Rows(state));
    }

    [Fact]
    public void ATreatedRowLeavesAfterTheLongFixedTime()
    {
        var (state, mail) = OneRow();
        state = state.Trash(mail.Id, Now).State;

        Assert.Single(Rows(state.RemoveExpiredRows(Now + state.Options.TreatedRowLifetime - TimeSpan.FromMinutes(1)).State));
        Assert.Empty(Rows(state.RemoveExpiredRows(Now + state.Options.TreatedRowLifetime).State));
    }

    [Fact]
    public void ATreatedRowLeavesWhenTheWindowIsClosed()
    {
        var (state, mail) = OneRow();
        state = state.Trash(mail.Id, Now).State;

        Assert.Empty(Rows(state.RemoveTreatedRows().State));
    }

    [Fact]
    public void DismissingALineSendsItsMailToTheTrashAndRemovesIt()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Informs("Remboursement de 42,10 € reçu")));

        var change = state.Dismiss(mail.Id, Now);

        Assert.Equal([mail.Id], Assert.IsType<MoveToTrash>(Assert.Single(change.Intents)).MailIds);
        Assert.Empty(TriageList.InformationLines(change.State, MailScope.All));
    }

    [Fact]
    public void DismissAllDismissesEveryLineOfThePageAsOneGesture()
    {
        var state = Sorted(
            (To(minutesAgo: 30), MailJudgment.Informs("Remboursement de 42,10 €")),
            (To(minutesAgo: 20), MailJudgment.Informs("Remboursement de 18,00 €")),
            (To(deliveredTo: "maison@exemple.fr", mailboxId: "maison", minutesAgo: 10), MailJudgment.Informs("Coupure d'eau jeudi")));

        var change = state.DismissAll(MailScope.Mailbox("perso"), Now);

        Assert.Equal(["Coupure d'eau jeudi"], TriageList.InformationLines(change.State, MailScope.All).Select(line => line.Sentence));
        Assert.Equal(2, change.Intents.Count);
        Assert.Equal(3, TriageList.InformationLines(change.State.UndoLastGesture(s_later).State, MailScope.All).Count);
    }

    [Fact]
    public void AGestureOnASubjectAlreadyTreatedDoesNothing()
    {
        var (state, mail) = OneRow();
        state = state.Trash(mail.Id, Now).State;

        Assert.True(state.AddTask(mail.Id, Today, s_later).IsUnchanged);
        Assert.True(state.Trash(mail.Id, s_later).IsUnchanged);
    }

    [Fact]
    public void TheCrossOfATreatedRowDoesNothingInTheInstantAfterTheGesture()
    {
        var (state, mail) = OneRow();
        state = state.Trash(mail.Id, Now).State;

        Assert.True(state.RemoveRow(mail.Id, Now + state.Options.ClickGuard / 2).IsUnchanged);
        Assert.False(state.RemoveRow(mail.Id, Now + state.Options.ClickGuard).IsUnchanged);
    }

    [Fact]
    public void TheCrossOfALineDoesNothingInTheInstantAfterItCameBack()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Informs("Remboursement de 42,10 € reçu")));
        state = state.Dismiss(mail.Id, Now).State;
        var back = Now + state.Options.ClickGuard;
        state = state.Undo(mail.Id, back).State;

        Assert.True(state.Dismiss(mail.Id, back + state.Options.ClickGuard / 2).IsUnchanged);
        Assert.False(state.Dismiss(mail.Id, back + state.Options.ClickGuard).IsUnchanged);
    }
}
