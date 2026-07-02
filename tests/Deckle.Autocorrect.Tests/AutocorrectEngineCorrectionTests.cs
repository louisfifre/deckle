using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

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
        Assert.Equal(("ca", "ça"), fail);
        Assert.Empty(h.Applied);
    }

    // Regression (JOURNAL 2026-07-02): a correction firing on an elision commit
    // must not double the apostrophe. The elision apostrophe lives INSIDE the
    // committed form (« j' ») and never showed as a separate boundary char, so
    // the diff carries no trailing boundary — before the fix the boundary was
    // appended anyway, sending « j'' » → « J'' » with an eaten letter.
    [Fact]
    public void AnElisionCorrectionDoesNotDoubleTheApostrophe()
    {
        using var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("j'", "J'"));
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();

        h.Type("j'"); // the apostrophe closes the elision and commits « j' »

        var call = Assert.Single(h.Injector.Calls);
        Assert.Equal(("j'", "J'"), call); // no trailing boundary, no doubled apostrophe
    }
}
