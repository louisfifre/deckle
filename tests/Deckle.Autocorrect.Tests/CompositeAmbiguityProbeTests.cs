using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class CompositeAmbiguityProbeTests
{
    [Fact]
    public void CorrectionTakebackKeepsOnlyTheOwningCandidateFamily()
    {
        var owner = new StubProbe(
            [new AccentVariant("deja", 0), new AccentVariant("déjà", 1000)]);
        var unrelated = new StubProbe(
            [new AccentVariant("deja", 0), new AccentVariant("deçà", 10)]);
        var composite = new CompositeAmbiguityProbe(owner, unrelated);

        IReadOnlyList<AccentVariant> candidates =
            composite.CorrectionCandidates("deja");

        Assert.Equal(["deja", "déjà"], candidates.Select(candidate => candidate.Form));
        Assert.Equal(1, owner.SentenceCallCount);
        Assert.Equal(0, unrelated.SentenceCallCount);
    }

    [Fact]
    public void UnsettledLiteralStillMergesCandidateFamilies()
    {
        var first = new StubProbe(
            [new AccentVariant("ura", 0), new AccentVariant("aura", 1000)]);
        var second = new StubProbe(
            [new AccentVariant("ura", 0), new AccentVariant("ira", 500)]);
        var composite = new CompositeAmbiguityProbe(first, second);

        IReadOnlyList<AccentVariant> candidates =
            composite.AmbiguousCandidates("ura");

        Assert.Equal(
            ["aura", "ira", "ura"],
            candidates.Select(candidate => candidate.Form));
    }

    private sealed class StubProbe(IReadOnlyList<AccentVariant> candidates)
        : IAmbiguityProbe
    {
        public int SentenceCallCount { get; private set; }

        public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
            candidates;

        public IReadOnlyList<AccentVariant> SentenceCandidates(
            string word,
            bool includeTypedLiteral)
        {
            SentenceCallCount++;
            return candidates;
        }
    }
}
