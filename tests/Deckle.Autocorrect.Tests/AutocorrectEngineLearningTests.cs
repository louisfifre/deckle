using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The learning signals around the decision: a word the engine LEAVES ALONE
// feeds adoption evidence, a CORRECTED word must not reinforce its own typo,
// and the content guards (length, digits, apostrophe, known lexicon forms) keep
// junk out. The personal dictionary runs for real on a temp file with a frozen
// clock, so clean occurrence counts are exact.
[Trait("Category", "integration")]
public sealed class AutocorrectEngineLearningTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"deckle-engine-pdict-{Guid.NewGuid():N}.json");
    private readonly DateTimeOffset _now = new(2026, 06, 13, 0, 0, 0, TimeSpan.Zero);

    private PersonalDictionary NewDictionary() => new(_path, () => _now);

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private AutocorrectEngineHarness Harness(
        PersonalDictionary dictionary,
        ICorrectionPolicy? policy = null,
        IFrequencyLexicon? french = null,
        IFrequencyLexicon? english = null)
    {
        var h = new AutocorrectEngineHarness(policy, dictionary, french, english);
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();
        return h;
    }

    [Fact]
    public void AWordTheEngineLeavesAloneIsAdoptedAsACommit()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict); // NeverCorrects

        h.Type("wibble ");

        Assert.Contains(dict.SnapshotWords(), w => w.Word == "wibble");
    }

    [Fact]
    public void ACorrectedWordDoesNotReinforceTheBareTypo()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict, ScriptedPolicy.Maps("ca", "ça"));

        h.Type("ca ");

        // The correction landed; the typo must not be learned, or repetition
        // would adopt it and silently disable its own correction.
        Assert.DoesNotContain(dict.SnapshotWords(), w => w.Word == "ca");
    }

    [Fact]
    public void AWordContainingADigitIsNotLearned()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict);

        h.Type("ab3 ");

        Assert.Empty(dict.SnapshotWords());
    }

    [Fact]
    public void ASingleLetterWordIsNotLearned()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict);

        h.Type("x ");

        Assert.Empty(dict.SnapshotWords());
    }

    [Fact]
    public void AWordEndingInAnApostropheIsNotLearned()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict);

        h.Type("abc' "); // "abc" is not an elision prefix, so the apostrophe joins it

        Assert.DoesNotContain(dict.SnapshotWords(), w => w.Word == "abc'");
    }

    [Fact]
    public void AWordAlreadyInTheFrenchLexiconIsNotLearned()
    {
        using var dict = NewDictionary();
        var french = FrequencyLexicon.LoadTsv(new StringReader("bonjour\t100\n"));
        using var h = Harness(dict, french: french);

        h.Type("bonjour ");

        Assert.Empty(dict.SnapshotWords()); // committed, then rejected as a known French form
    }

    [Fact]
    public void AValidEnglishSeedWordIsNotLearned()
    {
        using var dict = NewDictionary();
        var english = FrequencyLexicon.LoadTsv(new StringReader("hello\t0.1\n"));
        using var h = Harness(dict, english: english);

        h.Type("hello ");

        Assert.Empty(dict.SnapshotWords()); // committed, then rejected as a protected English seed form
    }

    [Fact]
    public void EngineLearningReadsThePrimaryLexiconThroughTheFrequencyInterface()
    {
        using var dict = NewDictionary();
        var primary = new StubFrequencyLexicon(new()
        {
            ["bonjour"] = 100,
        });
        using var h = Harness(dict, french: primary);

        h.Type("bonjour ");

        Assert.Empty(dict.SnapshotWords());
    }

    [Fact]
    public void TypingBareThenFixingAccentsByHandOnlyCountsTheRetypedWordOnce()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict); // NeverCorrects

        h.Type("widget "); // a neutral word: an ordinary commit, no edit — the baseline

        h.Type("cafe ");  // commit the bare form, opening the edit window
        h.Backspace();    // re-open "cafe" as the live buffer
        h.Backspace();    // drop the 'e'  → "caf"
        h.Type("é ");     // retype the accented form → commit "café" as an edit of "cafe"

        // The re-edit removes the bare occurrence from the clean count. The
        // accented word is a fresh clean commit, not a special adoption boost.
        double baseline = Assert.Single(dict.SnapshotWords(), w => w.Word == "widget").EffectiveWeight;
        var entry = Assert.Single(dict.SnapshotWords(), w => w.Word == "café");
        Assert.Equal(baseline, entry.EffectiveWeight, precision: 3);
        Assert.DoesNotContain(dict.SnapshotWords(), w => w.Word == "cafe" && w.EffectiveWeight > 0);
    }

    [Fact]
    public void AManualAccentFixOnANonEnrolledSurfaceIsNotLearned()
    {
        using var dict = NewDictionary();
        using var h = new AutocorrectEngineHarness(dictionary: dict);
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome"); // not enrolled
        h.Start();

        h.Type("cafe ");
        h.Backspace();
        h.Backspace();
        h.Type("é ");

        Assert.Empty(dict.SnapshotWords()); // gated: neither the commit nor the edit is learned
    }

    [Fact]
    public void AnEditToADifferentWordIsNotRecordedAsAnAccentFix()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict); // NeverCorrects

        h.Type("widget "); // a plain commit under the same clock — the ordinary-commit baseline

        h.Type("cat ");  // commit "cat", open the edit window
        h.Backspace();   // re-open "cat" as the live buffer
        h.Backspace();   // "ca"
        h.Backspace();   // "c"
        h.Backspace();   // "" — still re-opened (the boundary was eaten on the first Backspace)
        h.Type("dog ");  // recommit a DIFFERENT word: an edit, but not an accent fix

        // Fold("cat") != Fold("dog"), so the edit is just an edit, not accent
        // evidence. "dog" carries the ordinary commit count alone, equal to the
        // baseline.
        double baseline = Assert.Single(dict.SnapshotWords(), w => w.Word == "widget").EffectiveWeight;
        var dog = Assert.Single(dict.SnapshotWords(), w => w.Word == "dog");
        Assert.Equal(baseline, dog.EffectiveWeight, precision: 3);
    }

    [Fact]
    public void AnOverlongWordIsNotLearned()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict); // NeverCorrects

        h.Type(new string('a', 31) + " "); // 31 letters — past the 30-char content guard

        Assert.Empty(dict.SnapshotWords());
    }

    [Fact]
    public void ASuppressedPairIsWithheldWhateverThePolicyWants()
    {
        using var dict = NewDictionary();
        dict.RecordSuppression("ca", "ça"); // as the correction inlay's undo would write it
        using var h = Harness(dict, ScriptedPolicy.Maps("ca", "ça"));

        h.Type("ca "); // the policy WANTS to correct; the suppression must withhold it

        // A suppressed pair stays literal whatever the policy says — enforced in
        // the engine (CONTEXT.md), so even a policy without dictionary access
        // honors it. No correction applied, nothing injected.
        Assert.Empty(h.Applied);
        Assert.Empty(h.Injector.Calls);
    }

    private sealed class StubFrequencyLexicon(Dictionary<string, double> entries) : IFrequencyLexicon
    {
        public bool Contains(string lowerForm) => entries.ContainsKey(lowerForm);

        public double FrequencyOf(string lowerForm) =>
            entries.TryGetValue(lowerForm, out double frequency) ? frequency : 0.0;
    }
}
