using System.Collections.Generic;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The mistouch detector-generator — family KINDS interpreting per-user family
// RECORDS at the commit stage. These pin the conservative boundaries: an exact
// separator run or nothing, known words only on the repaired sides, abstention
// on reopened words and unknown runs, and inertness on a kind this build does
// not know (a data file from the future must not throw).
[Trait("Category", "unit")]
public class MistouchFamilyCorrectorTests
{
    private static readonly IReadOnlyList<MistouchFamilyRecord> Families = new[]
    {
        new MistouchFamilyRecord("sub ;→'", MistouchFamilyKinds.BoundaryApostrophe),
        new MistouchFamilyRecord("dropped space after ,", MistouchFamilyKinds.BoundaryMissingSpace, ","),
    };

    private static readonly HashSet<string> Words = new() { "il", "fait", "beau", "on" };

    private static MistouchFamilyCorrector NewCorrector() =>
        new(Families, w => Words.Contains(w));

    private static WordCommit Commit(
        string word, string? previous, string separators, bool reopened = false)
        => new(word, ' ', previous, null, 0, reopened, separators);

    [Fact]
    public void RepairsTheElisionApostropheSlip()
    {
        var repair = NewCorrector().Evaluate(Commit("il", previous: "qu", separators: ";"));

        Assert.NotNull(repair);
        Assert.Equal("qu;il", repair!.Original);
        Assert.Equal("qu'il", repair.Replacement);
        Assert.Equal("sub ;→'", repair.Signature);
    }

    [Fact]
    public void RepairsTheGluedComma()
    {
        var repair = NewCorrector().Evaluate(Commit("beau", previous: "fait", separators: ","));

        Assert.NotNull(repair);
        Assert.Equal("fait,beau", repair!.Original);
        Assert.Equal("fait, beau", repair.Replacement);
    }

    [Theory]
    [InlineData("il", "qu", ", ")]   // spaced comma — nothing glued
    [InlineData("il", "qu", "")]     // unknown run — abstain, never guess
    [InlineData("il", "fait", ";")]  // « fait » is no elision prefix
    [InlineData("xyzq", "qu", ";")]  // right side is no known word
    [InlineData("beau", "xyzq", ",")] // left side is no known word (identifier)
    [InlineData("il", null, ";")]    // no previous word at all
    public void AbstainsOutsideTheExactPattern(string word, string? previous, string separators)
    {
        Assert.Null(NewCorrector().Evaluate(Commit(word, previous, separators)));
    }

    [Fact]
    public void AbstainsOnReopenedWords()
    {
        // The deliberate keystroke asserts intent — commit-stage doctrine.
        Assert.Null(NewCorrector().Evaluate(
            Commit("il", previous: "qu", separators: ";", reopened: true)));
    }

    [Fact]
    public void AnUnknownKindIsInertNeverFatal()
    {
        var futureData = new[] { new MistouchFamilyRecord("sub a→b", "hologram_keyboard") };
        var corrector = new MistouchFamilyCorrector(futureData, _ => true);

        Assert.Null(corrector.Evaluate(Commit("il", previous: "qu", separators: ";")));
    }
}
