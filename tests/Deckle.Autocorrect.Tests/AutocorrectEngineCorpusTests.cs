using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The widened typed-sentence corpus feed. Two things matter: collection now spans
// every editable, non-password surface — enrollment no longer bounds it — and the
// only things that can withhold it are the password / editability gates and the
// dedicated consent toggle. Observed through AutocorrectTextRecorded, the single
// point where the corpus reaches the provider. Distinct word forms ("bonjour") keep
// these clear of the leak-assertion words the observability suite uses.
[Trait("Category", "integration")]
public sealed class AutocorrectEngineCorpusTests
{
    [Fact]
    public void FeedsTheCorpusOnAnUndecidedEditableSurface()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => true);
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome"); // never enrolled
        h.Start();

        h.Type("bonjour.");

        Assert.Contains(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText
            && PayloadValue(e, "process") is "chrome"
            && PayloadValue(e, "closure") is "sentence");
    }

    [Fact]
    public void FeedsTheCorpusOnADeclinedEditableSurface()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => true);
        h.Settings.Apps["chrome"] = false; // the user declined correction here
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome");
        h.Start();

        h.Type("bonjour.");

        // Enrollment no longer bounds collection: even a declined app is recorded.
        Assert.Contains(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText
            && PayloadValue(e, "process") is "chrome");
    }

    [Fact]
    public void NeverFeedsTheCorpusOnAPasswordSurface()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(textTelemetry: () => true);
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
        h.Prober.Surface = AutocorrectEngineHarness.Editable("chrome");
        h.Start();

        h.Type("bonjour.");

        Assert.DoesNotContain(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtAutocorrectText);
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
