using Xunit;

namespace Deckle.Autocorrect.Tests;

// The revert gesture: a correction lands and arms; the very next physical
// Backspace restores the literal, anything else disarms. CONTEXT.md correction
// revert — the Backspace that deletes the boundary right after a corrected word
// also undoes the correction.
[Trait("Category", "integration")]
public sealed class AutocorrectEngineRevertTests
{
    private static AutocorrectEngineHarness Corrected()
    {
        var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"));
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();
        h.Type("ca "); // lands the correction and arms the revert
        Assert.Single(h.Applied); // self-certify: a correction really armed before we test the disarm
        return h;
    }

    [Fact]
    public void ABackspaceRightAfterACorrectionRestoresTheLiteral()
    {
        using var h = Corrected();

        h.Backspace();

        Assert.Equal(("ça", "ca"), h.Injector.Calls[^1]); // replacement rewritten back to original
        var reverted = Assert.Single(h.Reverted);
        Assert.Equal(("ca", "ça"), reverted);
    }

    [Fact]
    public void TypingAnotherCharacterDisarmsTheRevert()
    {
        using var h = Corrected();

        h.Type("x");   // a keystroke other than Backspace disarms
        h.Backspace(); // now a plain edit, not a revert

        Assert.Empty(h.Reverted);
        Assert.DoesNotContain(("ça", "ca"), h.Injector.Calls);
    }

    [Fact]
    public void APointerInteractionDisarmsTheRevert()
    {
        using var h = Corrected();

        h.Pointer();
        h.Backspace();

        Assert.Empty(h.Reverted);
        Assert.DoesNotContain(("ça", "ca"), h.Injector.Calls);
    }

    [Fact]
    public void AFailedRevertInjectionRaisesInjectionFailedAsRevert()
    {
        using var h = Corrected();
        h.Injector.Result = false; // the boundary is already gone; the rewrite cannot land

        h.Backspace();

        var fail = Assert.Single(h.InjectionFailures);
        Assert.Equal(("ca", "ça", true), fail); // true: this failure is on the revert path
        Assert.Empty(h.Reverted);
    }

    [Fact]
    public void ABackspaceWithoutAPriorCorrectionIsAPlainEditNotARevert()
    {
        using var h = new AutocorrectEngineHarness(); // NeverCorrects → nothing arms
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();

        h.Type("ca ");
        h.Backspace();

        Assert.Empty(h.Reverted);
        Assert.Empty(h.InjectionFailures);
        Assert.Empty(h.Injector.Calls);
    }
}
