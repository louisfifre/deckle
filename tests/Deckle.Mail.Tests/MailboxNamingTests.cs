using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class MailboxNamingTests
{
    [Fact]
    public void AMailboxShowsTheNameItWasGiven()
    {
        Assert.Equal("Perso", MailboxNaming.NameOf(Perso));
    }

    [Fact]
    public void AMailboxAddedWithoutANameShowsItsAddress()
    {
        Assert.Equal("maison@example.com", MailboxNaming.NameOf(Maison));
        Assert.Equal("maison@example.com", MailboxNaming.NameOf(Maison with { Name = "  " }));
    }

    [Fact]
    public void AnAliasIsNamedByThePartBeforeTheAtWithACapital()
    {
        Assert.Equal("Shop", MailboxNaming.NameOf(Perso, new MailAlias("shop@example.com")));
        Assert.Equal("Facture", MailboxNaming.NameOf(Perso, new MailAlias("facture@example.com")));
    }

    [Fact]
    public void AnAliasRenamedByHandShowsItsNewName()
    {
        Assert.Equal("Achats", MailboxNaming.NameOf(Perso, new MailAlias("shop@example.com", "Achats")));
    }

    [Fact]
    public void WithTheCapitalOptionOffAnAliasShowsThePartBeforeTheAtAsWritten()
    {
        var mailbox = Perso with { CapitalizesAliasNames = false };

        Assert.Equal("shop", MailboxNaming.NameOf(mailbox, new MailAlias("shop@example.com")));
    }

    [Fact]
    public void InboxStandsForTheMainAddress()
    {
        Assert.Equal("Inbox", MailboxNaming.InboxName);
    }
}
