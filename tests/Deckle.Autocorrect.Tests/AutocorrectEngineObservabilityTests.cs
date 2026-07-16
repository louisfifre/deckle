using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The provider contract. Two things matter: the right events fire on a
// correction, and — the module hard rule — the typed words NEVER cross the
// provider. Every payload carries counts, lengths and reasons only. Assertions
// are presence-based and scan ALL captured events, so they hold even if another
// engine test emits concurrently.
[Trait("Category", "observability")]
public sealed class AutocorrectEngineObservabilityTests
{
    public AutocorrectEngineObservabilityTests()
        => OperationalLogAdmission.Configure(
            activity => activity == OperationalLogActivity.Autocorrect);

    [Fact]
    public void StartEmitsEngineStarted()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness();
        h.Prober.Surface = AutocorrectEngineHarness.Editable();

        h.Start();

        Assert.Contains(listener.Events, e => e.EventId == DeckleAutocorrectSource.EvtEngineStarted);
    }

    [Fact]
    public void ACorrectionEmitsAppliedAndDetailWithoutLeakingTheWords()
    {
        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Autocorrect);
        try
        {
            using var listener = new TestEventListener("Deckle-Autocorrect");
            using var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"));
            h.Prober.Surface = AutocorrectEngineHarness.Editable();
            h.Start();

            h.Type("ca ");

            var events = listener.Events;
            Assert.Contains(events, e => e.EventId == DeckleAutocorrectSource.EvtCorrectionApplied);
            // The detail carries the correction reason and the form lengths — counts,
            // never the words themselves (2 = len "ca" = len "ça").
            Assert.Contains(events, e =>
                e.EventId == DeckleAutocorrectSource.EvtCorrectionDetail
                && PayloadValue(e, "reason") is "LexicalGate"
                && PayloadValue(e, "original_len") is 2
                && PayloadValue(e, "replacement_len") is 2);
            AssertNoTypedWordLeaked(events, "ca", "ça");
        }
        finally
        {
            OperationalLogAdmission.Configure(static _ => false);
        }
    }

    [Fact]
    public void InjectionFailuresFormOneIncidentUntilASuccessRecoversIt()
    {
        using var listener = new TestEventListener("Deckle-Autocorrect");
        using var h = new AutocorrectEngineHarness(ScriptedPolicy.Maps("ca", "ça"));
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        h.Injector.Result = false;
        h.Start();

        h.Type("ca ca ");
        h.Injector.Result = true;
        h.Type("ca ");

        Assert.Single(listener.Events,
            e => e.EventId == DeckleAutocorrectSource.EvtInjectionIncident);
        Assert.Single(listener.Events,
            e => e.EventId == DeckleAutocorrectSource.EvtInjectionRecovered);
    }

    // The hard rule, asserted directly: no string payload on any captured event
    // equals a word the user typed or the engine produced. Reasons (enum names)
    // and the process name are metadata, not typed content.
    private static void AssertNoTypedWordLeaked(
        IReadOnlyList<EventWrittenEventArgs> events, params string[] words)
    {
        foreach (var ev in events)
            if (ev.Payload is { } payload)
                foreach (object? value in payload)
                    if (value is string s)
                        Assert.DoesNotContain(s, words);
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
