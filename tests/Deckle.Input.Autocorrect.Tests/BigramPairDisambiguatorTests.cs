using Deckle.Input.Autocorrect.Engine;
using Deckle.Input.Autocorrect.Lexicon;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The context model: a guarded argmax over per-candidate scores, backing off
// trigram → bigram → unigram, with add-one smoothing, a literal-bias defense on
// the bare form, an evidence gate, and a margin. Null — leave the literal — is
// the expected outcome whenever evidence is thin or the race close.
[Trait("Category", "unit")]
public class BigramPairDisambiguatorTests
{
    // The a/à slot: both fold to "a". The Form is all Choose reads; frequency
    // is carried but unused, so the values are illustrative.
    private static readonly AccentVariant Bare = new("a", 10000);
    private static readonly AccentVariant Grave = new("à", 9000);
    private static readonly IReadOnlyList<AccentVariant> Candidates = new[] { Bare, Grave };

    private static BigramPairDisambiguator FromRows(
        (string, string, string, long)[] rows, DisambiguatorOptions? options = null) =>
        new(rows, options);

    [Fact]
    public void ChoosesAccentedFormAfterFavoringContext()
    {
        // "à" dominates after "va"; the bare form has no support there.
        var d = FromRows(new[]
        {
            ("a", "à", "va", 20L),
            ("a", "à", "", 20L),
        });

        Assert.Equal("à", d.Choose(["va"], Candidates));
    }

    [Fact]
    public void ChoosesBareFormAfterFavoringContext()
    {
        // "a" dominates after "il". The subject is the bigram preference, not
        // the margin default — pinned explicitly to keep the arithmetic legible.
        var d = FromRows(new[]
        {
            ("a", "a", "il", 10L),
            ("a", "a", "", 10L),
            ("a", "à", "", 5L),
        }, new DisambiguatorOptions { MarginRatio = 3.0 });

        Assert.Equal("a", d.Choose(["il"], Candidates));
    }

    [Fact]
    public void FallsBackToUnigramWhenContextEmpty()
    {
        // No context — the per-slot unigram totals decide. "a" overwhelms "à".
        var d = FromRows(new[]
        {
            ("a", "a", "", 100L),
            ("a", "à", "", 10L),
        });

        Assert.Equal("a", d.Choose([], Candidates));
    }

    [Fact]
    public void FallsBackToUnigramWhenPrevIsUnseen()
    {
        // "qux" was never seen: no bigram row, so the unigram totals decide.
        var d = FromRows(new[]
        {
            ("a", "a", "", 100L),
            ("a", "à", "", 10L),
        });

        Assert.Equal("a", d.Choose(["qux"], Candidates));
    }

    [Fact]
    public void ReturnsNullWhenMarginNotMet()
    {
        // Bigrams after "x" are near-even (6 vs 6): even with the literal bias
        // the winner does not clear the margin.
        var d = FromRows(new[]
        {
            ("a", "a", "x", 6L),
            ("a", "à", "x", 6L),
            ("a", "a", "", 6L),
            ("a", "à", "", 6L),
        });

        Assert.Null(d.Choose(["x"], Candidates));
    }

    [Fact]
    public void ReturnsNullWhenEvidenceBelowMinimum()
    {
        // Total raw evidence is 2 (< MinEvidence 5): never guess from thin air,
        // however lopsided the smoothed ratio looks.
        var d = FromRows(new[]
        {
            ("a", "a", "", 1L),
            ("a", "à", "", 1L),
        });

        Assert.Null(d.Choose([], Candidates));
    }

    [Fact]
    public void ReturnsNullForFewerThanTwoCandidates()
    {
        var d = FromRows(new[] { ("a", "a", "", 100L) });

        Assert.Null(d.Choose(["il"], new[] { Bare }));
    }

    [Fact]
    public void LiteralBiasFlipsABorderlineCaseTowardTheBareForm()
    {
        // After "z": "a" leads "à" 12 to 4 — enough that the literal bias pushes
        // the legal bare form over the margin, not enough on its own.
        var rows = new[]
        {
            ("a", "a", "z", 12L),
            ("a", "à", "z", 4L),
            ("a", "a", "", 12L),
            ("a", "à", "", 4L),
        };

        // The subject is the bias lever alone — margin pinned at 3× both sides.
        // No bias (1.0): smoothed 13 vs 5 — below the 3× margin → null.
        var noBias = FromRows(rows, new DisambiguatorOptions { MarginRatio = 3.0, LiteralBias = 1.0 });
        Assert.Null(noBias.Choose(["z"], Candidates));

        // Bias 2.0: the bare form's score doubles to 26 vs 5 → "a".
        var biased = FromRows(rows, new DisambiguatorOptions { MarginRatio = 3.0, LiteralBias = 2.0 });
        Assert.Equal("a", biased.Choose(["z"], Candidates));
    }

    // ── Trigram backoff ─────────────────────────────────────────────────────

    // A slot where the two orders disagree: the bigram "la" favors "à" (30 vs 1),
    // but the trigram "de la" favors the bare "a" (20 vs 1). The richer context
    // wins when present, and the order knob lets us reproduce the bigram.
    private static readonly (string, string, string, long)[] OrderDisagreeRows = new[]
    {
        ("a", "a", "", 5L),
        ("a", "à", "", 5L),
        ("a", "à", "la", 30L),
        ("a", "a", "la", 1L),
        ("a", "a", "de la", 20L),
        ("a", "à", "de la", 1L),
    };

    [Fact]
    public void TrigramContextOverridesBigram()
    {
        // Context [de, la]: the trigram "de la" is present and favors "a".
        var d = FromRows(OrderDisagreeRows, new DisambiguatorOptions { MarginRatio = 3.0 });

        Assert.Equal("a", d.Choose(["de", "la"], Candidates));
    }

    [Fact]
    public void MaxContextOrderTwoIgnoresTrigram()
    {
        // Same rows, same context, but capped at order 2: the trigram is ignored
        // and the bigram "la" decides — "à". Proves the A/B knob.
        var d = FromRows(OrderDisagreeRows,
            new DisambiguatorOptions { MarginRatio = 3.0, MaxContextOrder = 2 });

        Assert.Equal("à", d.Choose(["de", "la"], Candidates));
    }

    [Fact]
    public void BacksOffToBigramWhenTrigramUnseen()
    {
        // Context [xx, la]: the trigram "xx la" was never seen, so the decision
        // backs off to the bigram "la" — "à".
        var d = FromRows(OrderDisagreeRows, new DisambiguatorOptions { MarginRatio = 3.0 });

        Assert.Equal("à", d.Choose(["xx", "la"], Candidates));
    }

    [Fact]
    public void ExposesSlotAndRowCounts()
    {
        var d = FromRows(new[]
        {
            ("a", "a", "il", 10L),
            ("a", "a", "", 10L),
            ("a", "à", "", 5L),
        });

        Assert.Equal(1, d.SlotCount);  // one folded key: "a"
        Assert.Equal(3L, d.RowCount);  // three (folded,variant,prev) triples
    }
}
