using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

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
        // Margin pinned at 3× so the verdict reflects the counts, not the
        // calibration default.
        var d = FromRows(new[]
        {
            ("a", "à", "va", 20L),
            ("a", "à", "", 20L),
        }, new DisambiguatorOptions { MarginRatio = 3.0 });

        Assert.Equal("à", d.Choose(["va"], Candidates));
    }

    [Fact]
    public void ChoosesBareFormAfterFavoringContext()
    {
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
        // Smoothed and with the default literal bias, the race after "x" is
        // 14 vs 7, below the 3× margin.
        var d = FromRows(new[]
        {
            ("a", "a", "x", 6L),
            ("a", "à", "x", 6L),
            ("a", "a", "", 6L),
            ("a", "à", "", 6L),
        }, new DisambiguatorOptions { MarginRatio = 3.0 });

        Assert.Null(d.Choose(["x"], Candidates));
    }

    [Fact]
    public void ReturnsNullWhenEvidenceBelowMinimum()
    {
        // One short of the real minimum, all evidence on one form: only the evidence
        // gate can refuse — if it stopped firing, the lopsided margin would clear.
        var min = new DisambiguatorOptions().MinEvidence;
        var d = FromRows(new[]
        {
            ("a", "a", "", (long)(min - 1)),
            ("a", "à", "", 0L),
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

        // No bias (1.0): smoothed 13 vs 5, below the 3× margin.
        var noBias = FromRows(rows, new DisambiguatorOptions { MarginRatio = 3.0, LiteralBias = 1.0 });
        Assert.Null(noBias.Choose(["z"], Candidates));

        // Bias 2.0: the bare form's score doubles to 26 vs 5.
        var biased = FromRows(rows, new DisambiguatorOptions { MarginRatio = 3.0, LiteralBias = 2.0 });
        Assert.Equal("a", biased.Choose(["z"], Candidates));
    }

    // ── Trigram backoff ─────────────────────────────────────────────────────

    // The two orders disagree: the bigram "la" favors "à", the trigram "de la"
    // favors the bare "a".
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
        var d = FromRows(OrderDisagreeRows, new DisambiguatorOptions { MarginRatio = 3.0 });

        Assert.Equal("a", d.Choose(["de", "la"], Candidates));
    }

    [Fact]
    public void MaxContextOrderTwoIgnoresTrigram()
    {
        var d = FromRows(OrderDisagreeRows,
            new DisambiguatorOptions { MarginRatio = 3.0, MaxContextOrder = 2 });

        Assert.Equal("à", d.Choose(["de", "la"], Candidates));
    }

    [Fact]
    public void BacksOffToBigramWhenTrigramUnseen()
    {
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

        Assert.Equal(1, d.SlotCount);
        Assert.Equal(3L, d.RowCount);
    }
}
