using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class FrenchSentenceRerankerTests
{
    private static AccentVariant[] LaLà() =>
        new[] { new AccentVariant("la", 10.0), new AccentVariant("là", 1.0) };

    [Fact]
    public void PicksLocativeLaAfterEtreAtGroupEnd()
    {
        var reranker = new FrenchSentenceReranker();

        RerankOutcome outcome = reranker.Rerank(
            new[] { "je", "suis", "la" }, slotIndex: 2, LaLà());

        Assert.Equal("là", outcome.Chosen);
    }

    [Fact]
    public void PicksLocativeLaAfterEtreAndBridgeWordAtGroupEnd()
    {
        var reranker = new FrenchSentenceReranker();

        RerankOutcome outcome = reranker.Rerank(
            new[] { "je", "suis", "déjà", "la" }, slotIndex: 3, LaLà());

        Assert.Equal("là", outcome.Chosen);
    }

    [Fact]
    public void LeavesArticleLaWhenRightContextExists()
    {
        var reranker = new FrenchSentenceReranker();

        RerankOutcome outcome = reranker.Rerank(
            new[] { "je", "suis", "la", "personne" }, slotIndex: 2, LaLà());

        Assert.Null(outcome.Chosen);
        Assert.Equal(RerankOutcome.AbstainReasons.NoRule, outcome.AbstainReason);
    }

    [Fact]
    public void LeavesLaWhenPreviousWordIsNotEtre()
    {
        var reranker = new FrenchSentenceReranker();

        RerankOutcome outcome = reranker.Rerank(
            new[] { "je", "vois", "la" }, slotIndex: 2, LaLà());

        Assert.Null(outcome.Chosen);
        Assert.Equal(RerankOutcome.AbstainReasons.NoRule, outcome.AbstainReason);
    }

    [Fact]
    public void DelegatesWhenNoDeterministicRuleApplies()
    {
        var inner = new FixedReranker("la");
        var reranker = new FrenchSentenceReranker(inner);

        RerankOutcome outcome = reranker.Rerank(
            new[] { "je", "vois", "la" }, slotIndex: 2, LaLà());

        Assert.Equal("la", outcome.Chosen);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public void WholeSentenceContractKeepsTheShortLocativeRuleGlobal()
    {
        var reranker = new FrenchSentenceReranker();

        RerankOutcome outcome = reranker.RerankSentence(
            new[] { "je", "suis", "la" },
            new[] { new SentenceEditCandidate(2, "là") });

        Assert.Equal("là", outcome.Chosen);
        Assert.Equal(2, outcome.ChosenSlotIndex);
    }

    private sealed class FixedReranker : ISentenceReranker
    {
        private readonly string _chosen;

        public FixedReranker(string chosen) => _chosen = chosen;

        public int Calls { get; private set; }

        public RerankOutcome Rerank(
            IReadOnlyList<string> sentence,
            int slotIndex,
            IReadOnlyList<AccentVariant> candidates)
        {
            Calls++;
            return new RerankOutcome(
                _chosen,
                Array.Empty<RerankCandidateScore>(),
                Margin: 1.0,
                Threshold: 0.0,
                AbstainReason: null);
        }
    }
}
