using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

/// <summary>
/// The state is immutable, so a list a caller hands in must not change it
/// afterwards behind the state's back.
/// </summary>
[Trait("Category", "unit")]
public class ImmutabilityTests
{
    [Fact]
    public void AMailKeepsTheReferencesItWasGiven()
    {
        var references = new List<string> { "<root@example.com>" };
        var mail = To() with { References = references };

        references.Add("<other@example.com>");

        Assert.Equal(["<root@example.com>"], mail.References);
    }

    [Fact]
    public void AMailboxKeepsTheAliasesItWasGiven()
    {
        var aliases = new List<MailAlias> { new("shop@exemple.fr") };
        var mailbox = Perso with { Aliases = aliases };

        aliases.Add(new MailAlias("facture@exemple.fr"));

        Assert.Single(mailbox.Aliases);
    }

    [Fact]
    public void AJudgmentKeepsTheMatterNumbersItWasGiven()
    {
        var numbers = new List<string> { "4521" };
        var judgment = MailJudgment.Asks("Colis 4521 au point relais") with { MatterNumbers = numbers };

        numbers.Add("9999");

        Assert.Equal(["4521"], judgment.MatterNumbers);
    }

    [Fact]
    public void AnIntentKeepsTheMailsItWasGiven()
    {
        var mailIds = new List<string> { "<1@example.com>" };
        var trash = new MoveToTrash("perso", mailIds);
        var restore = new RestoreFromTrash("perso", mailIds);

        mailIds.Add("<2@example.com>");

        Assert.Single(trash.MailIds);
        Assert.Single(restore.MailIds);
    }
}
