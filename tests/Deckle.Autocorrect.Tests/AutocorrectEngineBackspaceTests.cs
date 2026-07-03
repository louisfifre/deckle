using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Backspace after a correction is a PLAIN Backspace. The implicit-Backspace
// revert is retired (JOURNAL 2026-07-02): Backspace never restores the typo,
// never writes a suppression, and no time window governs it — a correction is
// taken back only through the correction inlay. What Backspace DOES still do is
// re-open the corrected word for re-editing, so the tracker's realignment holds.
[Trait("Category", "integration")]
public sealed class AutocorrectEngineBackspaceTests
{
    private static AutocorrectEngineHarness Corrected()
    {
        var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"));
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();
        h.Type("ca "); // the space commits and lands the correction
        Assert.Single(h.Applied); // self-certify: a correction really landed before we test the Backspace
        return h;
    }

    [Fact]
    public void ABackspaceRightAfterACorrectionDoesNotRestoreTheTypo()
    {
        using var h = Corrected();

        h.Backspace();

        // Exactly one injector call total — the correction itself. The Backspace
        // adds nothing: the typo is never rewritten back.
        var call = Assert.Single(h.Injector.Calls);
        Assert.Equal(("ca ", "ça "), call);
        Assert.DoesNotContain(("ça", "ca"), h.Injector.Calls); // no reverse rewrite
    }

    [Fact]
    public void ABackspaceRightAfterACorrectionWritesNoSuppression()
    {
        using var path = new TempDictPath();
        using var dict = new PersonalDictionary(path.Value);
        using var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"), dictionary: dict);
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();

        h.Type("ca "); // the correction lands
        h.Backspace(); // a plain Backspace — no undo, no learning

        Assert.False(dict.IsSuppressed("ca", "ça"));
        Assert.Empty(dict.SnapshotSuppressions());
    }

    // The Backspace re-opens the CORRECTED word (ReplaceLastCommitted realignment):
    // backspacing into it then retyping to a different form re-commits in the same
    // slot and emits WordEdit whose Original is « ça » — the corrected form, not the
    // typo « ca ». Proven purely through the tracker's own edit output.
    [Fact]
    public void ABackspaceReopensTheCorrectedWordNotTheTypo()
    {
        using var h = Corrected();

        WordEdit? edited = null;
        h.Tracker.WordEdited += e => edited = e;

        h.Backspace();  // re-opens the live buffer as « ça » (the corrected word)
        h.Backspace();  // « ç »
        h.Type("o ");   // re-commit « ço » — a different form, so an edit fires

        Assert.NotNull(edited);
        Assert.Equal("ça", edited!.Original);   // the corrected form was re-opened, not « ca »
        Assert.Equal("ço", edited.Replacement);
    }

    // No time window exists anymore: a Backspace long after the correction behaves
    // exactly like an immediate one — still a plain Backspace, still no reverse
    // rewrite. Time advances past the old 2 s window between the commit and the
    // Backspace to prove no window governs it.
    [Fact]
    public void ABackspaceLongAfterTheCorrectionBehavesIdentically()
    {
        using var h = Corrected();

        h.TimeMs += 10_000; // far past any former window

        h.Backspace();

        var call = Assert.Single(h.Injector.Calls);
        Assert.Equal(("ca ", "ça "), call);
        Assert.DoesNotContain(("ça", "ca"), h.Injector.Calls);
    }

    // A word the user reopened and retyped is exempt from the COMMIT stage: the
    // deliberate keystroke asserts intent, so the engine must leave the literal
    // alone — the commit policy is not even consulted. Retyping by hand the very
    // form the engine corrects must stick this time (the sentence stage, absent
    // from this rig, is what keeps the right to revise from full context).
    [Fact]
    public void AReopenedRetypedWordIsExemptFromTheCommitStage()
    {
        using var h = Corrected(); // typed « ca », corrected to « ça » — one correction landed
        var scripted = (ScriptedPolicy)h.Policy;
        Assert.Single(scripted.Calls); // self-certify: the commit stage ran once, for the first « ca »

        // Reopen the corrected « ça » and retype the bare « ca » by hand.
        h.Backspace(); // re-opens the live buffer as « ça »
        h.Backspace(); // « ç »
        h.Backspace(); // « » — empty, still a reopened slot
        h.Type("ca "); // re-commit « ca » — deliberately the form the policy corrects

        // The reopened commit skipped the policy entirely, and no second
        // correction landed: the hand-retyped literal stands.
        Assert.Single(scripted.Calls);    // still just the first « ca » — the reopen was exempt
        Assert.Single(h.Applied);         // still just the original correction
        Assert.Single(h.Injector.Calls);  // no re-correction was injected for the retype
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
