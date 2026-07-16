using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The typed-sentence corpus feed. Collection stays within the same enrolled,
// editable, non-password surface scope as correction and also requires its
// dedicated consent toggle. Observed through AutocorrectTextRecorded, the single
// point where the corpus reaches the provider. Distinct word forms ("bonjour") keep
// these clear of the leak-assertion words the observability suite uses.
[Trait("Category", "integration")]
public sealed class AutocorrectEngineCorpusTests
{
    [Fact]
    public void NeverFeedsTheCorpusOnAnUndecidedEditableSurface()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => true);
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome"); // never enrolled
        h.Start();

        h.Type("bonjour.");

        Assert.DoesNotContain(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText);
    }

    [Fact]
    public void NeverFeedsTheCorpusOnADeclinedEditableSurface()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => true);
        h.Settings.Apps["chrome"] = false; // the user declined correction here
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome");
        h.Start();

        h.Type("bonjour.");

        Assert.DoesNotContain(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText);
    }

    [Fact]
    public void KeepsAFragmentTailOutOfTheCorpusSentence()
    {
        // A pointer reset in the middle of a word throws the typed prefix away;
        // the word's tail then commits alone and used to open the next corpus
        // sentence as a fragment (« e Setting UX … »). The tracker now reports
        // the dropped partial and the corpus drops that suspect first word.
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => true);
        h.Settings.Apps["chrome"] = true;
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome");
        h.Start();

        h.Type("probl");
        h.Pointer();            // mid-word reset — the tail below is a fragment
        h.Type("eme bonjour.");

        Assert.Contains(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText
            && PayloadValue(e, "typed") is "bonjour.");
        Assert.DoesNotContain(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText
            && PayloadValue(e, "typed") is string typed && typed.Contains("eme"));
    }

    [Fact]
    public void NeverFeedsTheCorpusOnAPasswordSurface()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => true);
        h.Settings.Apps["chrome"] = true;
        h.Prober.Surface = AutocorrectEngineHarness.PasswordBox("chrome");
        h.Start();

        h.Type("bonjour.");

        Assert.DoesNotContain(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText);
    }

    [Fact]
    public void NeverFeedsTheCorpusWhenTheToggleIsOff()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => false);
        h.Settings.Apps["chrome"] = true;
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome");
        h.Start();

        h.Type("bonjour.");

        Assert.DoesNotContain(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText);
    }

    [Fact]
    public void DoesNotEmitAnAccumulatedRunAfterConsentIsWithdrawn()
    {
        bool consent = true;
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => consent);
        h.Settings.Apps["chrome"] = true;
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome");
        h.Start();

        h.Type("bonjour ");
        consent = false;
        h.Engine.ReconcileTextTelemetry();
        consent = true;
        h.Type("salut.");

        Assert.DoesNotContain(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText
            && PayloadValue(e, "typed") is string typed
            && typed.Contains("bonjour", StringComparison.Ordinal));
        Assert.Contains(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText
            && PayloadValue(e, "typed") is "salut.");
    }

    [Fact]
    public void AttributesAnInterruptedRunToTheSurfaceThatProducedIt()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => true);
        h.Prober.Surface = AutocorrectEngineHarness.Editable("notepad");
        h.Start();

        h.Type("bonjour ");
        h.RefocusOn(AutocorrectEngineHarness.Editable("chrome"));

        Assert.Contains(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText
            && PayloadValue(e, "process") is "notepad"
            && PayloadValue(e, "closure") is "interrupted");
    }

    [Fact]
    public void FoldsAReEditOnAnEnrolledSurfaceIntoTheOriginalSlot()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => true);
        h.Settings.Apps["chrome"] = true;
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome");
        h.Start();

        h.Type("bonjour ");
        for (int i = 0; i < 8; i++) h.Backspace(); // boundary, then seven letters
        h.Type("bonsoir ami.");

        Assert.Contains(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText
            && PayloadValue(e, "typed") is "bonjour ami."
            && PayloadValue(e, "final") is "bonsoir ami."
            && PayloadValue(e, "history") is string history
            && history.Contains("»user:bonsoir", StringComparison.Ordinal));
    }

    private static object? PayloadValue(EventWrittenEventArgs ev, string name)
    {
        var names = ev.PayloadNames;
        var payload = ev.Payload;
        if (names is null || payload is null) return null;
        for (int i = 0; i < names.Count; i++)
            if (names[i] == name) return payload[i];
        return null;
    }
}
