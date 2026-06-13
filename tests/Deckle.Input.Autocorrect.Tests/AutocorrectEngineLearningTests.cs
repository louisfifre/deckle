using System.IO;
using Deckle.Input.Autocorrect.Engine;
using Deckle.Input.Autocorrect.Learning;
using Deckle.Input.Autocorrect.Lexicon;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The learning signals around the decision: a word the engine LEAVES ALONE
// feeds adoption, a CORRECTED word must not reinforce its own typo, and the
// content guards (length, digits, apostrophe, known lexicon forms) keep junk
// out. The personal dictionary runs for real on a temp file with a frozen
// clock, so reinforcement weights are exact and decay is out of the picture.
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
        FrequencyLexicon? french = null,
        FrequencyLexicon? english = null)
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
    public void AKnownEnglishWordIsNotLearned()
    {
        using var dict = NewDictionary();
        var english = FrequencyLexicon.LoadTsv(new StringReader("hello\t300\n")); // far above the 0.5 ppm bar
        using var h = Harness(dict, english: english);

        h.Type("hello ");

        Assert.Empty(dict.SnapshotWords()); // committed, then rejected as a known English form
    }

    [Fact]
    public void TypingBareThenFixingAccentsByHandIsLearnedAsAManualFix()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict); // NeverCorrects

        h.Type("cafe ");  // commit the bare form, opening the edit window
        h.Backspace();    // re-open "cafe" as the live buffer
        h.Backspace();    // drop the 'e'  → "caf"
        h.Type("é ");     // retype the accented form → commit "café" as an edit of "cafe"

        // "café" carried BOTH the commit boost (1.0) and the manual-accent-fix
        // boost (1.5): a weight of 2.5 proves the manual-fix path fired, not
        // just the ordinary commit.
        var entry = Assert.Single(dict.SnapshotWords(), w => w.Word == "café");
        Assert.Equal(2.5, entry.EffectiveWeight, precision: 3);
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

        h.Type("cat ");  // commit "cat", open the edit window
        h.Backspace();   // re-open "cat" as the live buffer
        h.Backspace();   // "ca"
        h.Backspace();   // "c"
        h.Backspace();   // "" — still re-opened (the boundary was eaten on the first Backspace)
        h.Type("dog ");  // recommit a DIFFERENT word: an edit, but not an accent fix

        // Fold("cat") != Fold("dog"), so the manual-accent-fix boost (+1.5) must
        // NOT apply: "dog" carries the ordinary commit weight (1.0) alone.
        var dog = Assert.Single(dict.SnapshotWords(), w => w.Word == "dog");
        Assert.Equal(1.0, dog.EffectiveWeight, precision: 3);
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
    public void ARevertedPairStaysSuppressedOnTheNextType()
    {
        using var dict = NewDictionary();
        using var h = Harness(dict, ScriptedPolicy.Maps("ca", "ça"));

        h.Type("ca "); // the correction lands and arms the revert
        h.Backspace(); // revert → records the suppression ("ca","ça")

        Assert.True(dict.IsSuppressed("ca", "ça")); // precondition: the gesture registered
        int callsAfterRevert = h.Injector.Calls.Count;
        h.Applied.Clear();

        h.Type("ca "); // same pair — the suppression must withhold the correction

        // The policy still WANTS to correct, but the suppression overrides it:
        // a reverted pair stays literal whatever the policy says (CONTEXT.md).
        Assert.Empty(h.Applied);
        Assert.Equal(callsAfterRevert, h.Injector.Calls.Count); // no new injection
    }
}
