using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class SubjectTests
{
    [Fact]
    public void AReplyInTheSameThreadJoinsTheSubject()
    {
        var first = To(minutesAgo: 120);
        var reply = To(minutesAgo: 60) with { InReplyTo = first.Id };

        var state = Sorted(
            (first, MailJudgment.Asks("Envoyer l'attestation demandée par l'assureur")),
            (reply, MailJudgment.Asks("Envoyer l'attestation avant le 20")));

        var row = Assert.Single(Rows(state));
        Assert.Equal(2, state.Subjects[row.SubjectId].Mails.Count);
    }

    [Fact]
    public void TwoRepliesToAMailNeverSortedHereAreOneSubject()
    {
        var reply = To(minutesAgo: 120) with { References = ["<root@example.com>"] };
        var later = To(minutesAgo: 60) with { InReplyTo = "<other@example.com>", References = ["<root@example.com>", "<other@example.com>"] };

        var state = Sorted(
            (reply, MailJudgment.Asks("Confirmer le rendez-vous")),
            (later, MailJudgment.Asks("Confirmer le rendez-vous de jeudi")));

        Assert.Single(Rows(state));
    }

    [Fact]
    public void MailsThatCarryTheSameOrderNumberAreOneSubject()
    {
        // The order confirmation from a shop and the pickup notice from its carrier.
        var confirmation = To(deliveredTo: "shop@exemple.fr", from: "boutique@example.com", minutesAgo: 600);
        var pickup = To(deliveredTo: "shop@exemple.fr", from: "transporteur@example.com", minutesAgo: 60);

        var state = Sorted(
            (confirmation, MailJudgment.SetAside(SetAsideReason.Receipt, "Commande 4521 confirmée") with { MatterNumbers = ["4521"] }),
            (pickup, MailJudgment.Asks("Colis 4521 au point relais jusqu'au 18", On(11, 18)) with { MatterNumbers = ["4521"] }));

        var row = Assert.Single(Rows(state));
        Assert.Equal(2, state.Subjects[row.SubjectId].Mails.Count);
    }

    [Fact]
    public void AMatterNumberJoinsWhateverItsCase()
    {
        var state = Sorted(
            (To(minutesAgo: 600), MailJudgment.SetAside(SetAsideReason.Receipt, "Dossier AB-4521 ouvert") with { MatterNumbers = ["AB-4521"] }),
            (To(minutesAgo: 60), MailJudgment.Asks("Pièce manquante au dossier ab-4521") with { MatterNumbers = ["ab-4521"] }));

        Assert.Equal(2, state.Subjects[Assert.Single(Rows(state)).SubjectId].Mails.Count);
    }

    [Fact]
    public void MailsThatShareOnlyTheirSenderAreSeparateSubjects()
    {
        // Two refund statements from the same insurer are two lines; its request for a document is a third subject.
        var state = Sorted(
            (To(from: "assureur@example.com", minutesAgo: 300), MailJudgment.Informs("Remboursement de 42,10 €")),
            (To(from: "assureur@example.com", minutesAgo: 200), MailJudgment.Informs("Remboursement de 18,00 €")),
            (To(from: "assureur@example.com", minutesAgo: 100), MailJudgment.Asks("Envoyer l'attestation demandée par l'assureur")));

        Assert.Equal(2, TriageList.InformationLines(state, MailScope.All).Count);
        Assert.Single(Rows(state));
    }

    [Fact]
    public void ANewMailJoiningAWaitingSubjectSortsItAgainFromThatMail()
    {
        // A parcel on its way is set aside; « at the pickup point until the 18th » makes it a subject in Triage.
        var onItsWay = To(minutesAgo: 600);
        var atPickupPoint = To(minutesAgo: 60) with { InReplyTo = onItsWay.Id };

        var setAside = Sorted((onItsWay, MailJudgment.SetAside(SetAsideReason.NoActionNeeded, "Colis en route")));
        Assert.Equal(1, TriageList.SetAsideCount(setAside));

        var state = setAside.Sort(atPickupPoint, MailJudgment.Asks("Colis au point relais jusqu'au 18", On(11, 18)), Now).State;

        var row = Assert.Single(Rows(state));
        Assert.Equal("Colis au point relais jusqu'au 18", row.Sentence);
        Assert.Equal(0, TriageList.SetAsideCount(state));
    }

    [Fact]
    public void ALaterMailOfItsSubjectReplacesAnInformationLine()
    {
        var first = To(minutesAgo: 600);
        var update = To(minutesAgo: 60) with { InReplyTo = first.Id };

        var state = Sorted(
            (first, MailJudgment.Informs("Coupure d'eau jeudi")),
            (update, MailJudgment.Informs("Coupure d'eau reportée à vendredi")));

        var line = Assert.Single(TriageList.InformationLines(state, MailScope.All));
        Assert.Equal("Coupure d'eau reportée à vendredi", line.Sentence);
    }

    [Fact]
    public void AMailForASubjectAlreadyTreatedIsSortedAsANewMail()
    {
        var first = To(minutesAgo: 600);
        var reply = To(minutesAgo: 60) with { InReplyTo = first.Id };
        var state = Sorted((first, MailJudgment.Asks("Envoyer l'attestation demandée par l'assureur")));
        state = state.Trash(first.Id, Now).State;

        state = state.Sort(reply, MailJudgment.Asks("Attestation reçue, merci de signer"), Now).State;

        Assert.Single(state.Subjects[first.Id].Mails);
        Assert.Equal(2, Rows(state).Count);
    }

    [Fact]
    public void ARowCarriesTheSentenceThatAsksWhenALaterMailOnlyRepeatsTheRequest()
    {
        var request = To(minutesAgo: 600);
        var reminder = To(minutesAgo: 60) with { InReplyTo = request.Id };

        var state = Sorted(
            (request, MailJudgment.Asks("Envoyer l'attestation avant le 20", On(11, 20))),
            (reminder, MailJudgment.RepeatsEarlierRequest("Relance : attestation")));

        var row = Assert.Single(Rows(state));
        Assert.Equal("Envoyer l'attestation avant le 20", row.Sentence);
        Assert.Equal(new DateOnly(2026, 11, 18), row.ProposedDay);
    }
}
