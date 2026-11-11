using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class ListOrderTests
{
    [Fact]
    public void SubjectsWithADeadlineComeFirstTheNearestFirstThenTheOthersTheOldestFirst()
    {
        var state = Sorted(
            (To(minutesAgo: 50), MailJudgment.Asks("No deadline, newer")),
            (To(minutesAgo: 900), MailJudgment.Asks("No deadline, older")),
            (To(minutesAgo: 10), MailJudgment.Asks("Deadline on the 20th", On(11, 20))),
            (To(minutesAgo: 20), MailJudgment.Asks("Deadline on the 13th", On(11, 13))));

        Assert.Equal(
            ["Deadline on the 13th", "Deadline on the 20th", "No deadline, older", "No deadline, newer"],
            Rows(state).Select(row => row.Sentence));
    }

    [Fact]
    public void TheDateAMailArrivedNeverStandsForADeadline()
    {
        // Arrived today, it would come before a deadline in December if its arrival counted as one.
        var state = Sorted(
            (To(minutesAgo: 5), MailJudgment.Asks("Facture Infomaniak de novembre disponible")),
            (To(minutesAgo: 900), MailJudgment.Asks("Renouveler l'assurance avant le 20 décembre", On(12, 20))));

        Assert.Equal(
            ["Renouveler l'assurance avant le 20 décembre", "Facture Infomaniak de novembre disponible"],
            Rows(state).Select(row => row.Sentence));
        Assert.Null(Rows(state)[1].ProposedDay);
    }

    [Fact]
    public void ADeadlineComparesAfterNoDeadlineAsIComparableRequires()
    {
        Assert.True(On(11, 13).CompareTo(null) > 0);
    }

    [Fact]
    public void ATimeSlotsDeadlineIsItsEnd()
    {
        var judgment = MailJudgment.AsksWithin("Livraison jeudi entre 9 h et 12 h", On(11, 12, hour: 9), On(11, 12, hour: 12));

        Assert.Equal(On(11, 12, hour: 12), judgment.Deadline);
    }

    [Fact]
    public void OnTheSameDayADeadlineWithATimeComesBeforeADeadlineOfTheWholeDay()
    {
        var state = Sorted(
            (To(minutesAgo: 90), MailJudgment.Asks("Whole day", On(11, 12))),
            (To(minutesAgo: 10), MailJudgment.AsksWithin("Slot until noon", On(11, 12, hour: 9), On(11, 12, hour: 12))));

        Assert.Equal(["Slot until noon", "Whole day"], Rows(state).Select(row => row.Sentence));
    }

    [Fact]
    public void ATreatedRowStaysWhereItWas()
    {
        var state = Sorted(
            (To(minutesAgo: 30), MailJudgment.Asks("First", On(11, 13))),
            (To(minutesAgo: 20), MailJudgment.Asks("Second")),
            (To(minutesAgo: 10), MailJudgment.Asks("Third")));
        var before = Rows(state).Select(row => row.SubjectId).ToList();

        state = state.Trash(before[1], Now).State;

        Assert.Equal(before, Rows(state).Select(row => row.SubjectId));
        Assert.NotNull(Rows(state)[1].Result);
    }

    [Fact]
    public void TriageMixesEveryMailboxAndAMailboxPageKeepsItsOwn()
    {
        var state = Sorted(
            (To(deliveredTo: "maison@exemple.fr", mailboxId: "maison", minutesAgo: 30), MailJudgment.Asks("Maison")),
            (To(minutesAgo: 20), MailJudgment.Asks("Perso inbox")),
            (To(deliveredTo: "shop@exemple.fr", minutesAgo: 10), MailJudgment.Asks("Perso shop")));

        Assert.Equal(3, Rows(state).Count);
        Assert.Equal(["Perso inbox", "Perso shop"], Rows(state, MailScope.Mailbox("perso")).Select(row => row.Sentence));
        Assert.Equal(["Perso shop"], Rows(state, MailScope.Alias("perso", "shop@exemple.fr")).Select(row => row.Sentence));
    }
}
