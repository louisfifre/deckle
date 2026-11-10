using Xunit;

namespace Deckle.Mail.Hub.Tests;

// The sample presents each situation the window must be seen in, and the
// search finds a mail by its sender, its sentence or its text.
[Trait("Category", "unit")]
public sealed class SampleAndSearchTests
{
    [Fact]
    public void ANormalDayHasFourSubjectsThreeLinesAndMailSetAside()
    {
        var session = new TriageSession(new SampleMailHubSource(SampleScenario.NormalDay));

        Assert.Equal(4, session.WaitingCount(MailFilter.All));
        Assert.Equal(3, session.InfoLines.Count);
        Assert.True(session.SetAsideCount(MailFilter.All) > 0);
        Assert.Empty(session.Problems);
    }

    // Frame 2.5: a row the sorting could not read and a row whose sender
    // failed the checks, both waiting in Triage, each with its mark.
    [Fact]
    public void TheMarksDayHasOneWaitingSubjectOfEachMark()
    {
        var session = new TriageSession(new SampleMailHubSource(SampleScenario.Marks));

        TriageRow unreadable = Assert.Single(session.RowsIn(MailFilter.All), r => r.Subject.Mark == SubjectMark.Unreadable);
        TriageRow unverified = Assert.Single(session.RowsIn(MailFilter.All), r => r.Subject.Mark == SubjectMark.SenderNotVerified);
        Assert.True(unreadable.Subject.CanBecomeTask);
        Assert.False(unverified.Subject.CanBecomeTask);
        Assert.False(session.AddTask(unverified.Id, null));
    }

    [Fact]
    public void NoMailboxYetHasNothingAtAll()
    {
        MailHubSnapshot snapshot = new SampleMailHubSource(SampleScenario.NoMailbox).Load();

        Assert.Empty(snapshot.Mailboxes);
        Assert.Empty(snapshot.Subjects);
        Assert.Empty(snapshot.Activity);
    }

    [Fact]
    public void EveryAddressOfTheSampleIsOnAnExampleDomain()
    {
        foreach (SampleScenario scenario in Enum.GetValues<SampleScenario>())
        {
            MailHubSnapshot snapshot = new SampleMailHubSource(scenario).Load();
            IEnumerable<string> addresses = snapshot.Mailboxes.Select(m => m.Address)
                .Concat(snapshot.Mailboxes.SelectMany(m => m.Aliases).Select(a => a.Address))
                .Concat(snapshot.Subjects.SelectMany(s => s.Mails).Select(m => m.SenderAddress));
            Assert.All(addresses, a => Assert.True(
                a.EndsWith("@exemple.fr", StringComparison.Ordinal) || a.EndsWith(".example", StringComparison.Ordinal), a));
        }
    }

    [Fact]
    public void TheSearchFindsASenderAndSaysWhereItsSubjectStands()
    {
        var session = new TriageSession(new SampleMailHubSource(SampleScenario.NormalDay));

        IReadOnlyList<MailSearchHit> hits = MailSearch.Find(session, "martin");

        MailSearchHit hit = Assert.Single(hits);
        Assert.Equal("s-devis", hit.SubjectId);
        Assert.True(hit.Place.IsWaiting);
    }

    [Fact]
    public void TheSearchIgnoresAccentsAndFindsTreatedSubjects()
    {
        var session = new TriageSession(new SampleMailHubSource(SampleScenario.NormalDay));

        MailSearchHit hit = Assert.Single(MailSearch.Find(session, "echeance"));

        Assert.Equal("s-assurance", hit.SubjectId);
        Assert.Equal(GestureKind.Task, hit.Place.Gesture?.Kind);
    }

    [Theory]
    [InlineData("moi@exemple.fr", "Infomaniak")]
    [InlineData("someone@unknown.example", null)]
    public void AddMailboxFindsTheServerOfAKnownProvider(string address, string? provider)
    {
        Assert.Equal(provider, MailProviders.Find(address)?.Provider);
    }

    [Theory]
    [InlineData("moi@exemple.fr", true)]
    [InlineData("moi@", false)]
    [InlineData("moi exemple.fr", false)]
    public void AddMailboxWaitsForSomethingThatLooksLikeAnAddress(string address, bool expected)
    {
        Assert.Equal(expected, MailProviders.LooksLikeAddress(address));
    }
}
