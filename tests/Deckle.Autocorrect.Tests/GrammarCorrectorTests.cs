using System.Collections.Generic;
using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The grammar stage: subject–verb agreement on a valid-but-misconjugated word
// the earlier stages leave alone.
[Trait("Category", "unit")]
public class GrammarCorrectorTests
{
    // A manger paradigm (verb-only, verbOnly=1), its participle (also adjective,
    // 0) and "ferme" (also a noun, 0) — the ambiguity guard's cases.
    private const string Verbs =
        "mange\tmanger\tind:pre:1s;ind:pre:3s;sub:pre:1s;sub:pre:3s;imp:pre:2s\t1\n" +
        "manges\tmanger\tind:pre:2s;sub:pre:2s\t1\n" +
        "mangeons\tmanger\tind:pre:1p\t1\n" +
        "mangez\tmanger\tind:pre:2p;imp:pre:2p\t1\n" +
        "mangent\tmanger\tind:pre:3p;sub:pre:3p\t1\n" +
        "manger\tmanger\tinf\t1\n" +
        "mangé\tmanger\tpar:pas\t0\n" +
        "ferme\tfermer\timp:pre:2s;ind:pre:1s;ind:pre:3s\t0\n";

    private static GrammarCorrector Corrector(string? verbs = null, IPersonalLexicon? personal = null)
    {
        var morph = VerbMorphology.LoadTsv(new StringReader(verbs ?? Verbs));
        return new GrammarCorrector(morph, personal);
    }

    // ── Corrections that earned it ──────────────────────────────────────────

    [Fact]
    public void AddsTheMissingSecondPersonS()
    {
        var d = Corrector().Evaluate("mange", ["tu"]);

        Assert.NotNull(d);
        Assert.Equal("manges", d!.Replacement);
        Assert.Equal(CorrectionReason.SubjectVerbAgreement, d.Reason);
    }

    [Fact]
    public void FixesSingularVerbAfterPluralPronoun()
    {
        var d = Corrector().Evaluate("mange", ["ils"]);

        Assert.Equal("mangent", d!.Replacement);
        Assert.Equal(CorrectionReason.SubjectVerbAgreement, d.Reason);
    }

    [Fact]
    public void FixesSecondPersonFormAfterJe()
    {
        // "je manges" → "mange": both readings of "manges" (ind / sub) resolve to
        // the same 1s form, so the fix is unambiguous.
        var d = Corrector().Evaluate("manges", ["je"]);

        Assert.Equal("mange", d!.Replacement);
    }

    [Fact]
    public void FixesAfterFemininePluralPronoun()
    {
        Assert.Equal("mangent", Corrector().Evaluate("mange", ["elles"])!.Replacement);
    }

    [Fact]
    public void ReadsTheSubjectCaseInsensitively()
    {
        Assert.Equal("manges", Corrector().Evaluate("mange", ["Tu"])!.Replacement);
    }

    [Fact]
    public void AgreementLooksOnlyAtTheImmediatelyPrecedingWord()
    {
        Assert.Equal("manges", Corrector().Evaluate("mange", ["bonjour", "tu"])!.Replacement);
    }

    // ── Conservativity: the cases left alone ────────────────────────────────

    [Fact]
    public void AlreadyAgreeingVerbIsNeverTouched()
    {
        Assert.Null(Corrector().Evaluate("manges", ["tu"]));
    }

    [Fact]
    public void CorrectThirdPersonIsLeftAlone()
    {
        Assert.Null(Corrector().Evaluate("mange", ["il"]));
    }

    [Fact]
    public void NoSubjectPronounLeavesItAlone()
    {
        Assert.Null(Corrector().Evaluate("mange", ["le"]));
    }

    [Fact]
    public void NoLeftContextLeavesItAlone()
    {
        Assert.Null(Corrector().Evaluate("mange", []));
    }

    [Fact]
    public void VousAndNousAreNotTreatedAsSubjects()
    {
        // They double as preverbal object clitics ("il vous regarde"), so the rule
        // excludes them rather than risk a false correction.
        Assert.Null(Corrector().Evaluate("mange", ["vous"]));
        Assert.Null(Corrector().Evaluate("mange", ["nous"]));
    }

    [Fact]
    public void FormThatIsAlsoANounIsLeftAlone()
    {
        // "tu ferme" would agree as "tu fermes", but "ferme" is also a noun — the
        // ambiguity guard refuses it. A conservative miss, on purpose.
        Assert.Null(Corrector().Evaluate("ferme", ["tu"]));
    }

    [Fact]
    public void NonVerbWordIsLeftAlone()
    {
        Assert.Null(Corrector().Evaluate("chat", ["il"]));
    }

    [Fact]
    public void AbstainsWhenNoFormFillsTheAgreeingSlot()
    {
        // A defective paradigm with no 2nd-person-singular form: after "tu" there
        // is nothing safe to synthesise, so the literal stands.
        const string defective =
            "pluronly\tdeftest\tind:pre:3p\t1\n" +
            "singonly\tdeftest\tind:pre:3s\t1\n";
        Assert.Null(Corrector(defective).Evaluate("pluronly", ["tu"]));
    }

    [Fact]
    public void AdoptedWordShieldsItself()
    {
        var personal = new StubPersonal(adopted: new() { "mange" });
        Assert.Null(Corrector(personal: personal).Evaluate("mange", ["tu"]));
    }

    // ── Stub ────────────────────────────────────────────────────────────────

    private sealed class StubPersonal : IPersonalLexicon
    {
        private readonly HashSet<string> _adopted;

        public StubPersonal(HashSet<string>? adopted = null) => _adopted = adopted ?? new();

        public bool IsAdopted(string word) => _adopted.Contains(word.ToLowerInvariant());

        public bool IsSuppressed(string original, string replacement) => false;

        public IReadOnlyCollection<string> AdoptedWords => _adopted;
    }
}
