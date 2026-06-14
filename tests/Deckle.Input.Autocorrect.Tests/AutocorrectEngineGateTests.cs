using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The surface gates, in their doctrine order: injected events filtered first,
// the password gate cutting before decoding, and the editability / master /
// per-app decision gates withholding the action without stopping observation. A
// declined or non-editable surface never reaches the policy; a not-yet-decided
// one is evaluated only to offer enrollment, never to correct.
[Trait("Category", "integration")]
public sealed class AutocorrectEngineGateTests
{
    [Fact]
    public void KeystrokesOnAPasswordSurfaceAreNeverDecodedOrTracked()
    {
        var policy = ScriptedPolicy.Maps("ca", "ça");
        using var h = new AutocorrectEngineHarness(policy);
        h.Prober.Surface = AutocorrectEngineHarness.PasswordBox();
        h.Start();

        h.Type("ca"); // no boundary: were the gate open, the buffer would hold "ca"

        Assert.Equal(0, h.DecodeCharCount);       // the gate cut BEFORE decoding — no key was translated
        Assert.Equal("", h.Tracker.CurrentWord);  // and nothing was buffered
        Assert.Empty(policy.Calls);
        Assert.Empty(h.Injector.Calls);
    }

    [Fact]
    public void InjectedKeystrokesAreFilteredBeforeTracking()
    {
        var policy = ScriptedPolicy.Maps("ca", "ça");
        using var h = new AutocorrectEngineHarness(policy);
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();

        h.RaiseInjected('c');
        h.RaiseInjected('a'); // no boundary: were these not filtered, the buffer would hold "ca"

        Assert.Equal("", h.Tracker.CurrentWord); // our own repairs never feed the view
        Assert.Empty(policy.Calls);
    }

    [Fact]
    public void AnUndecidedProcessIsEvaluatedAndSuggestedButNeverCorrected()
    {
        var policy = ScriptedPolicy.Maps("ca", "ça");
        using var h = new AutocorrectEngineHarness(policy);
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome"); // never decided
        h.Start();

        h.Type("ca ");

        // The policy IS consulted on an undecided surface — that is how a
        // would-be correction is detected — but nothing is injected, and the
        // app is offered for enrollment exactly once.
        Assert.NotEmpty(policy.Calls);
        Assert.Empty(h.Injector.Calls);
        Assert.Equal(new[] { "chrome" }, h.EnrollmentSuggestions);
    }

    [Fact]
    public void ADeclinedProcessIsLeftEntirelyAlone()
    {
        var policy = ScriptedPolicy.Maps("ca", "ça");
        using var h = new AutocorrectEngineHarness(policy);
        h.Settings.Apps["chrome"] = false; // the user said no
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome");
        h.Start();

        h.Type("ca ");

        Assert.Empty(policy.Calls);            // decided-off: not even evaluated
        Assert.Empty(h.Injector.Calls);
        Assert.Empty(h.EnrollmentSuggestions); // and never prompted
    }

    [Fact]
    public void AnUndecidedProcessIsSuggestedOnlyOncePerRun()
    {
        var policy = ScriptedPolicy.Maps("ca", "ça");
        using var h = new AutocorrectEngineHarness(policy);
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome");
        h.Start();

        h.Type("ca ");
        h.Type("ca ");

        Assert.Equal(new[] { "chrome" }, h.EnrollmentSuggestions); // one prompt, not two
    }

    [Fact]
    public void ANonEditableSurfaceIsNeverCorrected()
    {
        var policy = ScriptedPolicy.Maps("ca", "ça");
        using var h = new AutocorrectEngineHarness(policy);
        h.Prober.Surface = AutocorrectEngineHarness.ReadOnly(); // enrolled process, but not text-editable
        h.Start();

        h.Type("ca ");

        Assert.Empty(policy.Calls);
        Assert.Empty(h.Injector.Calls);
    }

    [Fact]
    public void WhenTheModuleIsDisabledNothingReachesThePolicy()
    {
        var policy = ScriptedPolicy.Maps("ca", "ça");
        using var h = new AutocorrectEngineHarness(policy);
        h.Settings.Enabled = false;
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Start();

        h.Type("ca ");

        Assert.Empty(policy.Calls);
        Assert.Empty(h.Injector.Calls);
    }

    [Fact]
    public void TheSurfaceIsSeededOnStart()
    {
        using var h = new AutocorrectEngineHarness();
        h.Prober.Surface = AutocorrectEngineHarness.Editable("notepad");

        Assert.True(h.Start());

        Assert.True(h.Prober.ProbeCount >= 1);
        var seeded = Assert.Single(h.SurfaceChanges);
        Assert.Equal("notepad", seeded.Surface.ProcessName);
        Assert.True(seeded.Enrolled);
    }

    [Fact]
    public void AFocusChangeReprobesAndReportsTheNewSurface()
    {
        using var h = new AutocorrectEngineHarness();
        h.Prober.Surface = AutocorrectEngineHarness.Editable("notepad");
        h.Start();

        h.RefocusOn(AutocorrectEngineHarness.PasswordBox("notepad"));

        Assert.Equal(2, h.SurfaceChanges.Count);
        Assert.True(h.SurfaceChanges[^1].Surface.IsPassword);
        Assert.True(h.SurfaceChanges[^1].Enrolled); // still the notepad process
    }
}
