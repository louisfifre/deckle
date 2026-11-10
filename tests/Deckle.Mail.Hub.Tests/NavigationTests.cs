using Xunit;

namespace Deckle.Mail.Hub.Tests;

// The navigation as the person reads it: what it lists, the numbers it
// wears, folded and unfolded, and where back leads.
[Trait("Category", "unit")]
public sealed class NavigationTests
{
    private static TriageSession Open(SampleScenario scenario = SampleScenario.NormalDay)
        => new(new SampleMailHubSource(scenario));

    [Fact]
    public void TriageAndActivityComeFirstThenTheMailboxesThenSettings()
    {
        IReadOnlyList<RailEntry> rail = MailRail.Build(Open(), _ => true);

        Assert.Equal(
            [RailEntryKind.Triage, RailEntryKind.Activity, RailEntryKind.Group, RailEntryKind.Group, RailEntryKind.Mailbox, RailEntryKind.Settings],
            rail.Select(e => e.Kind));
    }

    [Fact]
    public void ActivityIsAbsentWhileThereIsNoActivityAtAll()
    {
        IReadOnlyList<RailEntry> rail = MailRail.Build(Open(SampleScenario.NoMailbox), _ => true);

        Assert.Equal([RailEntryKind.Triage, RailEntryKind.Settings], rail.Select(e => e.Kind));
    }

    [Fact]
    public void AFoldedGroupWearsItsMailboxTotalAndUnfoldedItsChildrenWearTheirs()
    {
        TriageSession session = Open();

        RailEntry folded = MailRail.Build(session, _ => false).Single(e => e.MailboxId == "mb-perso");
        RailEntry unfolded = MailRail.Build(session, _ => true).Single(e => e.MailboxId == "mb-perso");

        Assert.Equal(3, folded.Count);
        Assert.Null(unfolded.Count);
        Assert.Equal(["Inbox:3", "Facture:", "Shop:1", "Contact:"],
            unfolded.Children.Select(c => $"{(c.Kind == RailEntryKind.Inbox ? "Inbox" : c.Label)}:{c.Count}"));
    }

    [Fact]
    public void ATreatedSubjectLeavesTheNumbers()
    {
        TriageSession session = Open();
        session.MoveToTrash("s-drive");

        RailEntry perso = MailRail.Build(session, _ => true).Single(e => e.MailboxId == "mb-perso");

        Assert.Equal(2, perso.Children[0].Count);
        Assert.Null(perso.Children.Single(c => c.AliasId == "al-shop").Count);
    }

    [Fact]
    public void AMailboxWithoutANameShowsItsAddressAndAMailboxThatFailsSaysSo()
    {
        IReadOnlyList<RailEntry> rail = MailRail.Build(Open(SampleScenario.MailboxCannotConnect), _ => true);

        RailEntry maison = rail.Single(e => e.MailboxId == "mb-maison");
        Assert.Equal("maison@exemple.fr", maison.Label);
        Assert.True(maison.CannotConnect);
    }

    [Theory]
    [InlineData("shop@exemple.fr", true, "Shop")]
    [InlineData("facture@exemple.fr", true, "Facture")]
    [InlineData("shop@exemple.fr", false, "shop@exemple.fr")]
    public void AnAliasIsNamedFromItsAddressWithACapital(string address, bool fromAddress, string expected)
    {
        Assert.Equal(expected, MailNames.Alias(new MailAlias("a", address), fromAddress));
    }

    [Fact]
    public void ANameTheAliasWasGivenStays()
    {
        Assert.Equal("Boutique", MailNames.Alias(new MailAlias("a", "shop@exemple.fr", "Boutique"), true));
    }

    [Fact]
    public void BackIsDisabledOnTheFirstPageAndLeadsToThePreviousOne()
    {
        var history = new MailHistory();
        history.Reset(MailDestination.Triage);
        Assert.False(history.CanGoBack);

        history.Go(MailDestination.Subject("s-devis"));
        Assert.True(history.CanGoBack);

        Assert.Equal(MailDestination.Triage, history.Back());
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void ThePageAlreadyShownIsNotPushedTwice()
    {
        var history = new MailHistory();
        history.Reset(MailDestination.Triage);
        history.Go(MailDestination.Triage);

        Assert.Equal(1, history.Depth);
    }

    [Fact]
    public void BackSkipsAPageThatNoLongerExists()
    {
        var history = new MailHistory();
        history.Reset(MailDestination.Settings);
        history.Go(MailDestination.MailboxSettings("mb-gone"));
        history.Go(MailDestination.Mailbox("mb-gone"));
        history.Go(MailDestination.Settings);

        MailDestination? back = history.Back(d => d.MailboxId != "mb-gone");

        Assert.Equal(MailDestination.Settings, back);
        Assert.False(history.CanGoBack);
    }
}
