using Xunit;

namespace Deckle.Mail.Hub.Tests;

// A sentence shown where it wraps keeps an hour with its unit and a range of
// hours whole; nothing else of the sentence changes.
[Trait("Category", "unit")]
public sealed class TypesetTests
{
    private const char NoBreak = ' ';

    [Fact]
    public void ARangeOfHoursNeverBreaks()
    {
        string typeset = MailText.Typeset("Commande à retirer aujourd'hui, 17 h – 18 h");

        Assert.EndsWith($"17{NoBreak}h{NoBreak}–⁠{NoBreak}18{NoBreak}h", typeset);
        Assert.DoesNotContain(' ', typeset[typeset.IndexOf("17", StringComparison.Ordinal)..]);
        Assert.StartsWith("Commande à retirer aujourd'hui, ", typeset);
    }

    [Fact]
    public void ADashBetweenWordsStillBreaks()
    {
        Assert.Equal("Devis Martin – relance", MailText.Typeset("Devis Martin – relance"));
    }

    [Fact]
    public void AWordStartingWithHIsNotAUnit()
    {
        Assert.Equal("3 heures de retard", MailText.Typeset("3 heures de retard"));
    }
}
