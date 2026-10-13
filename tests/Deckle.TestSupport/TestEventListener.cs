using System.Diagnostics.Tracing;

namespace Deckle.TestSupport;

// ── TestEventListener ─────────────────────────────────────────────────────────
//
// Collects every event of the targeted provider, at every level and keyword;
// the test then asserts on the collected sequence.
//
// Dispose it (`using`): Dispose unregisters the listener, otherwise it keeps
// capturing emissions from following tests.
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
        // The null-check covers the calls made during base(), before
        // _providerName is assigned; the constructor registers those sources.
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
