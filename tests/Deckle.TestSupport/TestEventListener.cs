using System.Diagnostics.Tracing;

namespace Deckle.TestSupport;

// ── TestEventListener ─────────────────────────────────────────────────────────
//
// EventListener instrumented for observability tests. Collects
// EventWrittenEventArgs emitted by the targeted provider, without
// listener-side filtering; the test then asserts on the collected sequence.
//
// Native EventListener limitation to know: OnEventSourceCreated is called for
// pre-existing sources during the base class constructor, BEFORE derived class
// fields are assigned. The explicit re-scan through EventSource.GetSources()
// after name assignment covers this case. OnEventSourceCreated remains useful
// for sources created AFTER listener instantiation.
//
// Typical use in a test:
//   using var listener = new TestEventListener("Deckle-Chrono");
//   DeckleChronoSource.Log.PilotEmitted();
//   Assert.Single(listener.Events);
//
// The `using` is important: Dispose unregisters the listener, otherwise it
// keeps capturing emissions from following tests.
public sealed class TestEventListener : EventListener
{
    private readonly string _providerName;
    private readonly List<EventWrittenEventArgs> _events = new();
    private readonly object _gate = new();

    public TestEventListener(string providerName)
    {
        _providerName = providerName;

        // Catch up sources created BEFORE listener instantiation.
        // OnEventSourceCreated ran on these sources during base() with
        // _providerName still null; register them explicitly here.
        foreach (var source in EventSource.GetSources())
        {
            if (source.Name == providerName)
            {
                EnableEvents(source, EventLevel.LogAlways, EventKeywords.All);
            }
        }
    }

    public IReadOnlyList<EventWrittenEventArgs> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // Sources created AFTER listener instantiation: _providerName is now
        // assigned. The null-check protects the pre-existing case (pre-existing
        // sources are handled in the constructor).
        if (_providerName is not null && eventSource.Name == _providerName)
        {
            EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // Safety guard: BCL EventListener can route non-Deckle system events
        // (RuntimeEventSource, etc.) depending on passive EnableEvents. Filter
        // by name just in case.
        if (eventData.EventSource.Name != _providerName) return;

        lock (_gate)
        {
            _events.Add(eventData);
        }
    }
}
