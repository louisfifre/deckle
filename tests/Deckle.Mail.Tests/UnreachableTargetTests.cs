using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class UnreachableTargetTests
{
    private static (TriageState State, Mail Mail) OneRowWhile(MailTarget unreachable)
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Asks("Facture Infomaniak de novembre disponible")));
        return (state.TargetUnreachable(unreachable).State, mail);
    }

    [Fact]
    public void AGestureOnAnUnreachableMailboxIsKeptAndMarkedAsWaiting()
    {
        var (state, mail) = OneRowWhile(MailTarget.Mailbox("perso"));

        var change = state.Trash(mail.Id, Now);

        Assert.Empty(change.Intents);
        Assert.True(Assert.Single(Rows(change.State)).Result!.Waiting);
    }

    [Fact]
    public void AKeptGestureIsSentWhenItsTargetIsBack()
    {
        var (state, mail) = OneRowWhile(MailTarget.Mailbox("perso"));
        state = state.Trash(mail.Id, Now).State;

        var change = state.TargetReachable(MailTarget.Mailbox("perso"));

        Assert.Equal([mail.Id], Assert.IsType<MoveToTrash>(Assert.Single(change.Intents)).MailIds);
        Assert.False(Assert.Single(Rows(change.State)).Result!.Waiting);
    }

    [Fact]
    public void ATaskAddedWhileAnytypeIsUnreachableIsKeptAndTheTrashStillLeaves()
    {
        var (state, mail) = OneRowWhile(MailTarget.Anytype);

        var change = state.AddTask(mail.Id, Today, Now);

        Assert.IsType<MoveToTrash>(Assert.Single(change.Intents));
        Assert.IsType<CreateTask>(Assert.Single(change.State.HeldIntents));
        Assert.True(Assert.Single(Rows(change.State)).Result!.Waiting);
    }

    [Fact]
    public void UndoingAKeptGestureDropsItInsteadOfReversingIt()
    {
        var (state, mail) = OneRowWhile(MailTarget.Anytype);
        state = state.AddTask(mail.Id, Today, Now).State;

        var change = state.Undo(mail.Id, Now.AddSeconds(5));

        Assert.DoesNotContain(change.Intents, intent => intent is RemoveTask);
        Assert.Contains(change.Intents, intent => intent is RestoreFromTrash);
        Assert.Empty(change.State.HeldIntents);
    }

    [Fact]
    public void AnIntentThatDidNotArriveIsKeptForWhenItsTargetIsBack()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Asks("Facture Infomaniak de novembre disponible")));
        var trashed = state.Trash(mail.Id, Now);

        state = trashed.State.ReportUndelivered(trashed.Intents[0]).State;

        Assert.True(Assert.Single(Rows(state)).Result!.Waiting);
        Assert.Equal(trashed.Intents, state.TargetReachable(MailTarget.Mailbox("perso")).Intents);
    }

    [Fact]
    public void AnUndeliveredIntentOfAnUndoneGestureIsDropped()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Asks("Facture Infomaniak de novembre disponible")));
        var trashed = state.Trash(mail.Id, Now);
        state = trashed.State.Undo(mail.Id, Now.AddSeconds(5)).State;

        // The trash is reported lost after the row was already given back.
        state = state.ReportUndelivered(trashed.Intents[0]).State;

        Assert.Empty(state.HeldIntents);
        Assert.Empty(state.TargetReachable(MailTarget.Mailbox("perso")).Intents);
    }

    [Fact]
    public void AnUndeliveredReverseIntentIsDroppedOnceTheSubjectIsTreatedAgain()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Asks("Facture Infomaniak de novembre disponible")));
        state = state.Trash(mail.Id, Now).State;
        var undone = state.Undo(mail.Id, Now.AddSeconds(5));
        state = undone.State.Trash(mail.Id, Now.AddSeconds(10)).State;

        // The restore of the Undo is reported lost after the second trash went out.
        state = state.ReportUndelivered(Assert.IsType<RestoreFromTrash>(Assert.Single(undone.Intents))).State;

        Assert.Empty(state.HeldIntents);
        Assert.False(Assert.Single(Rows(state)).Result!.Waiting);
    }
}
