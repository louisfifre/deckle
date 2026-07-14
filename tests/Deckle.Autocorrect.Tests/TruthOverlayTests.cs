using System.Linq;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The ground-truth review sheet: a stable-keyed markdown table the maintainer fills
// by hand. These pin the invariants the overlay leans on — keys stable across runs,
// a lossless round-trip for the truth column, and a regeneration that preserves
// already-filled cells for keys that still exist.
[Trait("Category", "unit")]
public sealed class TruthOverlayTests
{
    private static TruthReviewRow Row(string key, string truth) =>
        new(key, "je vais a la banque.", "a", "à", "à", truth);

    [Fact]
    public void KeyIsStableAndSlotSpecific()
    {
        Assert.Equal(TruthOverlay.Key("il a dit.", 1), TruthOverlay.Key("il a dit.", 1));
        Assert.NotEqual(TruthOverlay.Key("il a dit.", 1), TruthOverlay.Key("il a dit.", 2));
        Assert.NotEqual(TruthOverlay.Key("il a dit.", 1), TruthOverlay.Key("il y a.", 1));
    }

    [Fact]
    public void RoundTripsThroughTheMarkdownSheet()
    {
        var rows = new[] { Row("aaaa1111", "à"), Row("bbbb2222", "") };

        var back = TruthOverlay.Parse(TruthOverlay.Render(rows)).ToList();

        Assert.Equal(2, back.Count);
        Assert.Equal("aaaa1111", back[0].Key);
        Assert.Equal("à", back[0].Truth);
        Assert.Equal("", back[1].Truth);
        Assert.Equal("a", back[0].TypedForm);
        Assert.Equal("à", back[0].FinalForm);
        Assert.Equal("à", back[0].JudgePick);
    }

    [Fact]
    public void ResolvedTruthsReturnsOnlyFilledCells()
    {
        var rows = new[] { Row("aaaa1111", "à"), Row("bbbb2222", "") };

        var resolved = TruthOverlay.ResolvedTruths(rows);

        Assert.True(resolved.TryGetValue("aaaa1111", out string? v));
        Assert.Equal("à", v);
        Assert.False(resolved.ContainsKey("bbbb2222"));
    }

    // Regeneration keeps the sheet's hand-filled cells alive: a key still disagreeing
    // carries its resolved truth forward even when the fresh pass leaves it blank, a
    // brand-new key stays blank, and a key that no longer disagrees drops out.
    [Fact]
    public void MergePreservesFilledTruthsForKeysStillPresent()
    {
        var existing = new[] { Row("aaaa1111", "à"), Row("bbbb2222", "où") };
        var fresh = new[] { Row("aaaa1111", ""), Row("cccc3333", "") }; // bbbb dropped, cccc new

        var merged = TruthOverlay.Merge(fresh, existing);

        Assert.Equal(2, merged.Count);
        Assert.Equal("à", merged.Single(r => r.Key == "aaaa1111").Truth); // preserved
        Assert.Equal("", merged.Single(r => r.Key == "cccc3333").Truth);  // new, blank
        Assert.DoesNotContain(merged, r => r.Key == "bbbb2222");          // gone
    }

    [Fact]
    public void MergeCollapsesDuplicateKeys()
    {
        var fresh = new[] { Row("aaaa1111", ""), Row("aaaa1111", "") };

        var merged = TruthOverlay.Merge(fresh, System.Array.Empty<TruthReviewRow>());

        Assert.Single(merged);
    }

    // A pipe in the free-text sentence must not break the table: it is neutralized on
    // write, and the key and truth columns still round-trip.
    [Fact]
    public void SanitizesAPipeInTheSentenceCell()
    {
        var rows = new[] { new TruthReviewRow("aaaa1111", "a | b sentence", "a", "à", "à", "à") };

        var back = TruthOverlay.Parse(TruthOverlay.Render(rows)).ToList();

        TruthReviewRow r = Assert.Single(back);
        Assert.Equal("aaaa1111", r.Key);
        Assert.Equal("à", r.Truth);
        Assert.DoesNotContain("|", r.FinalSentence);
    }
}
