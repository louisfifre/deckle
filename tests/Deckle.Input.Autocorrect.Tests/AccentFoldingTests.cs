using Deckle.Input.Autocorrect.Lexicon;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The canonical key: folding must collapse case, diacritics and the two French
// ligatures so every variant of a word lands under one lookup key. These pin
// the contract the gate and the index both rely on.
[Trait("Category", "unit")]
public class AccentFoldingTests
{
    [Fact]
    public void FoldStripsAccentsAndLowercases()
    {
        Assert.Equal("eleve", AccentFolding.Fold("Élève"));
        Assert.Equal("francais", AccentFolding.Fold("Français"));
    }

    [Fact]
    public void FoldExpandsLigatures()
    {
        Assert.Equal("oeuvre", AccentFolding.Fold("Œuvre"));
        Assert.Equal("oeuf", AccentFolding.Fold("œuf"));
        Assert.Equal("naevus", AccentFolding.Fold("nævus"));
    }

    [Fact]
    public void FoldFastPathLeavesPlainLowercaseUntouched()
    {
        // A plain ASCII lowercase word needs no work — same instance back.
        const string s = "marche";
        Assert.Same(s, AccentFolding.Fold(s));
    }

    [Fact]
    public void StripDiacriticsPreservesCase()
    {
        // The eval's QWERTY-US typist keeps the capital: É → E, not e.
        Assert.Equal("Eleve", AccentFolding.StripDiacritics("Élève"));
        Assert.Equal("FRANCAIS", AccentFolding.StripDiacritics("FRANÇAIS"));
        Assert.Equal("Oeuvre", AccentFolding.StripDiacritics("Œuvre"));
    }

    [Fact]
    public void HasDiacriticsDetectsAccentsAndLigatures()
    {
        Assert.True(AccentFolding.HasDiacritics("déjà"));
        Assert.True(AccentFolding.HasDiacritics("École"));
        Assert.True(AccentFolding.HasDiacritics("cœur"));
    }

    [Fact]
    public void HasDiacriticsFalseForPlainAscii()
    {
        Assert.False(AccentFolding.HasDiacritics("ecole"));
        Assert.False(AccentFolding.HasDiacritics("MARCHE"));
    }
}
