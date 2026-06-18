using System;
using System.IO;
using System.Linq;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// VerbMorphology reads the build-data verb artifact: for a surface form, the
// verb readings it carries (lemma + mode/tense/person); in reverse, the single
// form a lemma takes at a slot; and whether the surface doubles as a non-verb
// (ambiguous → the agreement rule stands aside). These assert the parsing and
// the two lookups, against a small hand-built manger paradigm.
[Trait("Category", "unit")]
public class VerbMorphologyTests
{
    // `form<TAB>lemma<TAB>infover<TAB>verbOnly`. A manger paradigm (verb-only),
    // its infinitive and participle (the participle doubles as an adjective, so
    // verbOnly=0), plus "ferme" which is also a noun (verbOnly=0).
    private const string Tsv =
        "mange\tmanger\tind:pre:1s;ind:pre:3s;sub:pre:1s;sub:pre:3s;imp:pre:2s\t1\n" +
        "manges\tmanger\tind:pre:2s;sub:pre:2s\t1\n" +
        "mangeons\tmanger\tind:pre:1p\t1\n" +
        "mangez\tmanger\tind:pre:2p;imp:pre:2p\t1\n" +
        "mangent\tmanger\tind:pre:3p;sub:pre:3p\t1\n" +
        "manger\tmanger\tinf\t1\n" +
        "mangé\tmanger\tpar:pas\t0\n" +
        "ferme\tfermer\timp:pre:2s;ind:pre:1s;ind:pre:3s\t0\n";

    private static VerbMorphology Load(string? tsv = null) =>
        VerbMorphology.LoadTsv(new StringReader(tsv ?? Tsv));

    [Fact]
    public void ParsesReadingsWithLemmaModeTensePerson()
    {
        var readings = Load().Analyses("manges");

        Assert.Equal(2, readings.Count);
        Assert.All(readings, r => Assert.Equal("manger", r.Lemma));
        Assert.Contains(readings, r => r is { Mode: "ind", Tense: "pre", PersonNumber: "2s" });
        Assert.Contains(readings, r => r is { Mode: "sub", Tense: "pre", PersonNumber: "2s" });
    }

    [Fact]
    public void ParsesInfinitiveAndParticipleWithFewerParts()
    {
        // "inf" carries no tense/person; "par:pas" carries no person.
        var inf = Assert.Single(Load().Analyses("manger"));
        Assert.Equal(("manger", "inf", "", ""), (inf.Lemma, inf.Mode, inf.Tense, inf.PersonNumber));

        var par = Assert.Single(Load().Analyses("mangé"));
        Assert.Equal(("manger", "par", "pas", ""), (par.Lemma, par.Mode, par.Tense, par.PersonNumber));
    }

    [Fact]
    public void NonVerbFormHasNoReadings()
    {
        Assert.False(Load().IsVerb("chat"));
        Assert.Empty(Load().Analyses("chat"));
    }

    [Fact]
    public void ConjugateFindsTheSlotForm()
    {
        var vb = Load();

        Assert.Equal("mange", vb.Conjugate("manger", "ind", "pre", "1s"));
        Assert.Equal("manges", vb.Conjugate("manger", "ind", "pre", "2s"));
        Assert.Equal("mangent", vb.Conjugate("manger", "ind", "pre", "3p"));
    }

    [Fact]
    public void ConjugateReturnsNullForAnUnfilledSlot()
    {
        // The paradigm has no future tense — the slot is empty, not guessed.
        Assert.Null(Load().Conjugate("manger", "ind", "fut", "1s"));
    }

    [Fact]
    public void ConjugateReturnsNullWhenTwoFormsFillOneSlot()
    {
        // Two distinct surfaces claim the same cell: synthesis must be unique to
        // be safe, so the clashing slot resolves to null, never a coin-flip.
        const string clashing =
            "forma\txtest\tind:pre:1s\t1\n" +
            "formb\txtest\tind:pre:1s\t1\n";
        Assert.Null(Load(clashing).Conjugate("xtest", "ind", "pre", "1s"));
    }

    [Fact]
    public void AmbiguousFlagsFormsThatDoubleAsNonVerbs()
    {
        var vb = Load();

        // "ferme" (verbOnly=0) is also a noun; "mangé" (0) also an adjective.
        Assert.True(vb.IsAmbiguous("ferme"));
        Assert.True(vb.IsAmbiguous("mangé"));
        // A verb-only form is not ambiguous.
        Assert.False(vb.IsAmbiguous("manges"));
    }

    [Fact]
    public void MalformedLinesAreSkippedAndCounted()
    {
        const string withJunk =
            "# a comment\n" +
            "\n" +
            "mange\tmanger\tind:pre:1s\t1\n" +
            "short\tline\n";        // too few fields — skipped, not a crash

        var vb = Load(withJunk);

        Assert.True(vb.IsVerb("mange"));
        Assert.Equal(1, vb.SkippedLines);  // only the short line; comment/blank are not skips
    }

    [Fact]
    public void FormsAreKeyedLowercase()
    {
        // The store lowercases on load, so a lookup by lowercased literal hits.
        Assert.True(Load("Mange\tmanger\tind:pre:1s\t1\n").IsVerb("mange"));
    }
}
