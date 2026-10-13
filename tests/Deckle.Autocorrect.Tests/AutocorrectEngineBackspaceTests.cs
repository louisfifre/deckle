using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Backspace after a correction is a PLAIN Backspace: it never restores the typo,
// never writes a suppression, and no time window governs it. A correction is
// taken back only through the correction inlay. Backspace still re-opens the
// corrected word for re-editing, so the tracker's realignment holds.
[Trait("Category", "integration")]
public sealed class AutocorrectEngineBackspaceTests
{
    private static AutocorrectEngineHarness Corrected()
    {
        var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"));
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();
        h.Type("ca ");
        Assert.Single(h.Applied); // self-certify: a correction really landed before we test the Backspace
        return h;
    }

    [Fact]
    public void ABackspaceRightAfterACorrectionDoesNotRestoreTheTypo()
    {
        using var h = Corrected();

        h.Backspace();

        var call = Assert.Single(h.Injector.Calls);
        Assert.Equal(("ca ", "ça "), call);
        Assert.DoesNotContain(("ça", "ca"), h.Injector.Calls);
    }

    [Fact]
    public void ABackspaceRightAfterACorrectionWritesNoSuppression()
    {
        using var path = new TempDictPath();
        using var dict = new PersonalDictionary(path.Value);
        using var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"), dictionary: dict);
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();

        h.Type("ca ");
        h.Backspace();

        Assert.False(dict.IsSuppressed("ca", "ça"));
        Assert.Empty(dict.SnapshotSuppressions());
    }

    // The re-opened word is the one ReplaceLastCommitted realigned, so a retyped
    // form re-commits in the same slot with the corrected form as its Original.
    [Fact]
    public void ABackspaceReopensTheCorrectedWordNotTheTypo()
    {
        using var h = Corrected();

        WordEdit? edited = null;
        h.Tracker.WordEdited += e => edited = e;

        h.Backspace();
        h.Backspace();
        h.Type("o ");   // a different form, so an edit fires

        Assert.NotNull(edited);
        Assert.Equal("ça", edited!.Original);
        Assert.Equal("ço", edited.Replacement);
    }

    [Fact]
    public void ABackspaceLongAfterTheCorrectionBehavesIdentically()
    {
        using var h = Corrected();

        h.TimeMs += 10_000;

        h.Backspace();

        var call = Assert.Single(h.Injector.Calls);
        Assert.Equal(("ca ", "ça "), call);
        Assert.DoesNotContain(("ça", "ca"), h.Injector.Calls);
    }

    // A reopened, retyped word is exempt from the commit stage: the deliberate
    // keystroke asserts intent, so the policy is not even consulted. The sentence
    // stage, absent from this rig, keeps the right to revise it from full context.
    [Fact]
    public void AReopenedRetypedWordIsExemptFromTheCommitStage()
    {
        using var h = Corrected();
        var scripted = (ScriptedPolicy)h.Policy;
        Assert.Single(scripted.Calls); // self-certify: the commit stage ran once, for the first « ca »

        h.Backspace();
        h.Backspace();
        h.Backspace(); // empty, still a reopened slot
        h.Type("ca "); // deliberately the form the policy corrects

        Assert.Single(scripted.Calls);
        Assert.Single(h.Applied);
        Assert.Single(h.Injector.Calls);
    }

    private sealed class TempDictPath : IDisposable
    {
        public string Value { get; } = Path.Combine(
            Path.GetTempPath(), $"deckle-backspace-pdict-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            if (File.Exists(Value)) File.Delete(Value);
        }
    }
}
