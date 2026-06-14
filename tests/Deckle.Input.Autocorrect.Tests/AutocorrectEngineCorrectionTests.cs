using Deckle.Input.Autocorrect;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The apply path of the conductor: a committed word, an actionable surface, a
// policy verdict → a single injected diff and a CorrectionApplied signal. Drives
// the real decode → track → decide → inject chain; only the OS ports are faked.
[Trait("Category", "integration")]
public sealed class AutocorrectEngineCorrectionTests
{
    [Fact]
    public void CommittingAWordOnAnEnrolledSurfaceAppliesThePolicyVerdict()
    {
        using var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"));
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        Assert.True(h.Start());

        h.Type("ca "); // the space commits the word

        var call = Assert.Single(h.Injector.Calls);
        Assert.Equal(("ca ", "ça "), call); // the boundary rides into the diff
        var applied = Assert.Single(h.Applied);
        Assert.Equal("ca", applied.Original);
        Assert.Equal("ça", applied.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, applied.Reason);
    }

    [Fact]
    public void TheLiveWordIsNotCorrectedUntilABoundaryCommitsIt()
    {
        using var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"));
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();

        h.Type("ca"); // no boundary yet

        Assert.Empty(h.Injector.Calls);
        Assert.Empty(h.Applied);
    }

    [Fact]
    public void AWordThePolicyLeavesAloneIsNeverInjected()
    {
        using var h = new AutocorrectEngineHarness(); // NeverCorrects
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();

        h.Type("hello ");

        Assert.Empty(h.Injector.Calls);
        Assert.Empty(h.Applied);
    }

    [Fact]
    public void TheLeftContextIsHandedToThePolicy()
    {
        var policy = new ScriptedPolicy((_, _) => null);
        using var h = new AutocorrectEngineHarness(policy);
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();

        h.Type("bonjour ca ");

        // Two words committed; the second carried the first as its left context.
        Assert.Contains(("bonjour", (string?)null), policy.Calls);
        Assert.Contains(("ca", (string?)"bonjour"), policy.Calls);
    }

    [Fact]
    public void AFailedInjectionRaisesInjectionFailedAndAppliesNothing()
    {
        using var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"));
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Injector.Result = false; // SendInput reports a partial/blocked burst
        h.Start();

        h.Type("ca ");

        var fail = Assert.Single(h.InjectionFailures);
        Assert.Equal(("ca", "ça", false), fail); // false: a forward correction, not a revert
        Assert.Empty(h.Applied);
    }
}
