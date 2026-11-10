using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class WhereAMailLandsTests
{
    [Fact]
    public void AMailThatAsksIsASubjectInTriage()
    {
        var state = Sorted((To(), MailJudgment.Asks("Facture Infomaniak de novembre disponible")));

        var row = Assert.Single(Rows(state));
        Assert.Equal("Facture Infomaniak de novembre disponible", row.Sentence);
        Assert.Empty(TriageList.InformationLines(state, MailScope.All));
        Assert.Equal(0, TriageList.SetAsideCount(state));
    }

    [Fact]
    public void AMailThatStatesAFactIsASecondaryInformationLine()
    {
        var state = Sorted((To(), MailJudgment.Informs("Remboursement de 42,10 € reçu")));

        var line = Assert.Single(TriageList.InformationLines(state, MailScope.All));
        Assert.Equal("Remboursement de 42,10 € reçu", line.Sentence);
        Assert.Equal("Service", line.Sender);
        Assert.Empty(Rows(state));
    }

    [Fact]
    public void AMailThatAsksNothingAndChangesNothingIsSetAsideWithItsReason()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de novembre")));

        Assert.Empty(Rows(state));
        Assert.Empty(TriageList.InformationLines(state, MailScope.All));
        Assert.Equal(1, TriageList.SetAsideCount(state));
        var line = Assert.Single(Assert.Single(ActivityLog.BySorting(state)).Lines);
        Assert.Equal(SetAsideReason.Newsletter, line.Reason);
        Assert.Equal(mail.Id, line.MailId);
    }

    [Fact]
    public void AMailTheSortingIsNotSureToSetAsideWaitsInTriage()
    {
        var state = Sorted((To(), MailJudgment.SetAside(SetAsideReason.NoActionNeeded, "Point sur votre dossier", doubtful: true)));

        Assert.Single(Rows(state));
        Assert.Equal(0, TriageList.SetAsideCount(state));
    }

    [Fact]
    public void AnUnreadableMailWaitsInTriageWithItsOwnSubjectAndItsReason()
    {
        var mail = To() with { SubjectLine = "Votre code de retrait" };
        var state = Sorted((mail, MailJudgment.CouldNotRead(UnreadableReason.ImagesOnly)));

        var row = Assert.Single(Rows(state));
        Assert.Equal("Votre code de retrait", row.Sentence);
        Assert.Equal(UnreadableReason.ImagesOnly, row.Unreadable);
    }

    [Fact]
    public void AnUnverifiedSenderWaitsInTriageAndOffersTheTrashOnly()
    {
        var mail = To() with { SenderVerified = false };
        var state = Sorted((mail, MailJudgment.SetAside(SetAsideReason.Promotion, "Offre exclusive")));

        var row = Assert.Single(Rows(state));
        Assert.False(row.SenderVerified);
        Assert.False(row.CanAddTask);
        Assert.True(state.AddTask(row.SubjectId, Today, Now).IsUnchanged);
        Assert.False(state.Trash(row.SubjectId, Now).IsUnchanged);
    }

    [Fact]
    public void AnUnverifiedSenderShowsItsAddressInPlaceOfTheNameItGaveItself()
    {
        var mail = To(from: "banque@example.com") with { SenderVerified = false };
        var state = Sorted((mail, MailJudgment.Informs("Votre compte est suspendu")));

        Assert.Equal("banque@example.com", state.Subjects[mail.Id].ShownSender);
    }

    [Fact]
    public void ARowOffersNoTaskWhileAnytypeIsNotConnected()
    {
        var state = Sorted((To(), MailJudgment.Asks("Facture Infomaniak de novembre disponible")));

        var row = Assert.Single(TriageList.Rows(state, MailScope.All, Now, tasksAvailable: false));
        Assert.False(row.CanAddTask);
    }

    [Fact]
    public void ARowShowsThePaperclipWhenAMailOfTheSubjectCarriesARealFile()
    {
        var state = Sorted(
            (To(), MailJudgment.Asks("Facture Infomaniak de novembre disponible")),
            (To(minutesAgo: 30) with { HasAttachment = true }, MailJudgment.Asks("Pièce justificative demandée")));

        Assert.Contains(Rows(state), row => row.HasAttachment);
        Assert.Contains(Rows(state), row => !row.HasAttachment);
    }

    [Fact]
    public void TheSameMailSortedTwiceIsPlacedOnce()
    {
        var mail = To();
        var state = Sorted((mail, MailJudgment.Asks("Facture Infomaniak de novembre disponible")));

        var change = state.Sort(mail, MailJudgment.Asks("Facture Infomaniak de novembre disponible"), Now);

        Assert.True(change.IsUnchanged);
        Assert.Single(change.State.Subjects[mail.Id].Mails);
    }
}
