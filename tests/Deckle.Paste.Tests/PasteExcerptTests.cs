using Xunit;

namespace Deckle.Paste.Tests;

// The beginning of a pasted text, as its row shows it.
[Trait("Category", "unit")]
public sealed class PasteExcerptTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Hello", "Hello")]
    [InlineData("  first line\r\n\tsecond  line  ", "first line second line")]
    public void AnExcerptIsOneLineWithItsSpacesMadeOne(string text, string excerpt)
    {
        Assert.Equal(excerpt, PasteExcerpt.Of(text));
    }

    [Fact]
    public void ALongTextIsCutWithAnEllipsis()
    {
        string excerpt = PasteExcerpt.Of(new string('a', 10_000));

        Assert.Equal(PasteExcerpt.MaxLength, excerpt.Length);
        Assert.EndsWith("…", excerpt);
    }

    [Fact]
    public void TheCutNeverSplitsACharacterOutsideTheBasicPlane()
    {
        // Each emoji is two UTF-16 units: the limit falls inside one.
        string text = new string('a', PasteExcerpt.MaxLength - 2) + string.Concat(Enumerable.Repeat("😀", 10));

        string excerpt = PasteExcerpt.Of(text);

        Assert.False(char.IsHighSurrogate(excerpt[^2]));
        Assert.EndsWith("…", excerpt);
    }

    [Fact]
    public void ATextJustAtTheLimitIsKeptWhole()
    {
        string text = new('a', PasteExcerpt.MaxLength);

        Assert.Equal(text, PasteExcerpt.Of(text));
    }
}
