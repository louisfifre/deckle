using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class NavigationCountsTests
{
    // The spec's example: three subjects wait in Perso, one of them through the alias shop@.
    private static TriageState ThreeWaitingInPerso() => Sorted(
        (To(minutesAgo: 30), MailJudgment.Asks("Facture Infomaniak de novembre disponible")),
        (To(minutesAgo: 20), MailJudgment.Asks("Envoyer l'attestation demandée par l'assureur")),
        (To(deliveredTo: "shop@exemple.fr", minutesAgo: 10), MailJudgment.Asks("Colis au point relais jusqu'au 18")));

    [Fact]
    public void AFoldedGroupHeaderCarriesTheSubjectsWaitingInTheMailboxAliasesIncluded()
    {
        var counts = NavigationCounts.For(ThreeWaitingInPerso(), Perso, folded: true);

        Assert.Equal(3, counts.Mailbox);
    }

    [Fact]
    public void UnfoldedTheHeaderCarriesNoNumberAndItsChildrenCarryTheirs()
    {
        var counts = NavigationCounts.For(ThreeWaitingInPerso(), Perso, folded: false);

        Assert.Null(counts.Mailbox);
        Assert.Equal(1, counts.Aliases["shop@exemple.fr"]);
    }

    [Fact]
    public void UnfoldedInboxCarriesTheTotalOfTheMailbox()
    {
        var state = ThreeWaitingInPerso();

        Assert.Equal(
            NavigationCounts.For(state, Perso, folded: true).Mailbox,
            NavigationCounts.For(state, Perso, folded: false).Inbox);
    }

    [Fact]
    public void TwoAliasesThatDifferOnlyByCaseAreOneAddress()
    {
        var mailbox = Perso with { Aliases = [new MailAlias("Shop@exemple.fr"), new MailAlias("shop@exemple.fr")] };

        var counts = NavigationCounts.For(ThreeWaitingInPerso(), mailbox, folded: false);

        Assert.Equal(1, Assert.Single(counts.Aliases).Value);
    }

    [Fact]
    public void AnItemWhereNothingWaitsHasNoNumber()
    {
        var counts = NavigationCounts.For(ThreeWaitingInPerso(), Perso, folded: false);

        Assert.Null(counts.Aliases["facture@exemple.fr"]);
        Assert.Null(NavigationCounts.For(ThreeWaitingInPerso(), Maison, folded: true).Mailbox);
    }

    [Fact]
    public void AMailboxWithNoAliasIsASingleItemWithItsCount()
    {
        var state = Sorted((To(deliveredTo: "maison@exemple.fr", mailboxId: "maison"), MailJudgment.Asks("Maison")));

        var counts = NavigationCounts.For(state, Maison, folded: false);

        Assert.Equal(1, counts.Mailbox);
        Assert.Null(counts.Inbox);
        Assert.Empty(counts.Aliases);
    }

    [Fact]
    public void TreatedMailIsNeverCounted()
    {
        var state = ThreeWaitingInPerso();
        state = state.Trash(Rows(state)[0].SubjectId, Now).State;

        Assert.Equal(2, NavigationCounts.For(state, Perso, folded: true).Mailbox);
    }

    [Fact]
    public void SetAsideMailAndInformationLinesAreNeverCounted()
    {
        var state = Sorted(
            (To(minutesAgo: 30), MailJudgment.SetAside(SetAsideReason.Newsletter, "Lettre de novembre")),
            (To(minutesAgo: 20), MailJudgment.Informs("Remboursement de 42,10 €")));

        Assert.Null(NavigationCounts.For(state, Perso, folded: true).Mailbox);
    }
}
