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
        using var h = Harness(dict);

        h.Type("wibble ");

        Assert.Contains(dict.SnapshotWords(), w => w.Word == "wibble");
    }

    [Fact]
    public void ACorrectedWordDoesNotReinforceTheBareTypo()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict, ScriptedPolicy.Maps("ca", "ça"));

        h.Type("ca ");

        // The typo must not be learned, or repetition would adopt it and
        // silently disable its own correction.
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

        Assert.Empty(dict.SnapshotWords());
    }

    [Fact]
    public void AValidEnglishSeedWordIsNotLearned()
    {
        using var dict = NewDictionary();
        var english = FrequencyLexicon.LoadTsv(new StringReader("hello\t0.1\n"));
        using var h = Harness(dict, english: english);

        h.Type("hello ");

        Assert.Empty(dict.SnapshotWords());
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
        using var h = Harness(dict);

        h.Type("widget ");

        h.Type("cafe ");
        h.Backspace();    // eats the boundary, re-opening "cafe"
        h.Backspace();
        h.Type("é ");

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
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome");
        h.Start();

        h.Type("cafe ");
        h.Backspace();
        h.Backspace();
        h.Type("é ");

        Assert.Empty(dict.SnapshotWords());
    }

    [Fact]
    public void AnEditToADifferentWordIsNotRecordedAsAnAccentFix()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict);

        h.Type("widget ");

        h.Type("cat ");
        h.Backspace();   // eats the boundary, re-opening "cat"
        h.Backspace();
        h.Backspace();
        h.Backspace();   // empty, still re-opened
        h.Type("dog ");

        // Fold("cat") != Fold("dog"), so the edit is not accent evidence.
        double baseline = Assert.Single(dict.SnapshotWords(), w => w.Word == "widget").EffectiveWeight;
        var dog = Assert.Single(dict.SnapshotWords(), w => w.Word == "dog");
        Assert.Equal(baseline, dog.EffectiveWeight, precision: 3);
    }

    [Fact]
    public void AnOverlongWordIsNotLearned()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict);

        h.Type(new string('a', 31) + " "); // past the 30-char content guard

        Assert.Empty(dict.SnapshotWords());
    }

    [Fact]
    public void ASuppressedPairIsWithheldWhateverThePolicyWants()
    {
        using var dict = NewDictionary();
        dict.RecordSuppression("ca", "ça"); // as the correction inlay's undo would write it
        using var h = Harness(dict, ScriptedPolicy.Maps("ca", "ça"));

        h.Type("ca ");

        // Enforced in the engine, so even a policy without dictionary access
        // honors the suppression.
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
