using Xunit;

namespace Deckle.Transcription.Parakeet.Decoding;

[Trait("Category", "unit")]
public sealed class LexiconBoostTreeTests
{
    // A small cased vocabulary in the detokenizer's form: a leading space opens a word.
    private static readonly string[] Texts = ["", " de", " De", "ck", "le", " dé", "s", " la", " Louis", " Fi", "fre", " "];
    private const int De = 1, DeUpper = 2, Ck = 3, Le = 4, DeAccent = 5, S = 6, La = 7, Louis = 8, Fi = 9, Fre = 10, Space = 11;
    private const float Strength = 2f;

    private static LexiconBoostTree Tree(params string[] terms) => LexiconBoostTree.Build(terms, Texts, Strength)!;

    [Fact]
    public void NoUsableTermGivesNoTree()
    {
        Assert.Null(LexiconBoostTree.Build(["", "  "], Texts, Strength));
    }

    [Fact]
    public void AWordStartIsBoostedInBothCases()
    {
        var tree = Tree("Deckle");

        Assert.True(tree.BonusOf(LexiconBoostTree.Root, De) > 0);
        Assert.Equal(tree.BonusOf(LexiconBoostTree.Root, De), tree.BonusOf(LexiconBoostTree.Root, DeUpper));
    }

    [Fact]
    public void ATokenThatLeavesTheTermIsNotBoosted()
    {
        var tree = Tree("Deckle");

        Assert.Equal(0f, tree.BonusOf(LexiconBoostTree.Root, La));
        Assert.Equal(0f, tree.BonusOf(LexiconBoostTree.Root, DeAccent));
        // A piece that does not open a word cannot start a term.
        Assert.Equal(0f, tree.BonusOf(LexiconBoostTree.Root, Ck));
    }

    [Fact]
    public void TheBonusGrowsAlongTheTermAndReachesTheStrengthAtItsEnd()
    {
        var tree = Tree("Deckle");

        int afterDe = tree.Advance(LexiconBoostTree.Root, De);
        int afterCk = tree.Advance(afterDe, Ck);
        float first = tree.BonusOf(LexiconBoostTree.Root, De);
        float second = tree.BonusOf(afterDe, Ck);
        float last = tree.BonusOf(afterCk, Le);

        Assert.True(first < second && second < last);
        Assert.Equal(Strength, last, 5);
    }

    [Fact]
    public void ACompletedTermSendsTheMatchBackToTheRoot()
    {
        var tree = Tree("Deckle");

        int state = tree.Advance(tree.Advance(tree.Advance(LexiconBoostTree.Root, De), Ck), Le);

        Assert.Equal(LexiconBoostTree.Root, state);
    }

    [Fact]
    public void ATokenOutsideTheMatchDropsIt()
    {
        var tree = Tree("Deckle");

        int state = tree.Advance(tree.Advance(LexiconBoostTree.Root, De), S);

        Assert.Equal(LexiconBoostTree.Root, state);
    }

    [Fact]
    public void ATokenThatOpensATermRestartsTheMatch()
    {
        var tree = Tree("Deckle");
        int fresh = tree.Advance(LexiconBoostTree.Root, De);

        int restarted = tree.Advance(fresh, DeUpper);

        Assert.Equal(fresh, restarted);
    }

    [Fact]
    public void APhraseCrossesTheSpaceBetweenItsWords()
    {
        var tree = Tree("Louis Fifre");

        int state = tree.Advance(LexiconBoostTree.Root, Louis);
        Assert.True(tree.BonusOf(state, Fi) > 0);
        state = tree.Advance(state, Fi);
        Assert.Equal(Strength, tree.BonusOf(state, Fre), 5);
    }

    [Fact]
    public void ACommonFirstPieceGetsLessThanTheWholeStrength()
    {
        // " de" is a frequent French word: starting "Deckle" with it must stay a small push.
        var tree = Tree("Deckle");

        Assert.True(tree.BonusOf(LexiconBoostTree.Root, De) < Strength / 2);
    }

    [Fact]
    public void DuplicateTermsCountOnce()
    {
        Assert.Equal(1, Tree("Deckle", "deckle", "DECKLE").TermCount);
    }
}
