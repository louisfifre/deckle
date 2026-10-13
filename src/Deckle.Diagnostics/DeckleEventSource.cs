using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Abstract base for every Deckle.* EventSource. Each emitting module
// derives one concrete provider (DeckleAudioSource, DeckleWhispSource,
// …) and declares its [Event(...)] methods directly. Deckle.Diagnostics
// itself hosts only cross-cutting sub-providers: non-business platform
// wiring that two or more modules use with the same parameters.
//
// No Log() catch-all method: every distinct call site becomes its own
// [Event(...)] method on the concrete provider. A generic
// Log(string, EventLevel) here would be the escape hatch that rots the
// typed discipline; a trivial event is a parameterless [Event] method.
//
// Session id. Shared by every provider for the life of the process and
// stamped on every JSONL row, so rows group by process run without piping
// the id through every event signature.
public abstract class DeckleEventSource : EventSource
{
    private static string? _sessionId;
    private static readonly object _sessionLock = new();

    public static string SessionId
    {
        get
        {
            if (_sessionId is not null) return _sessionId;
            lock (_sessionLock)
            {
                if (_sessionId is null)
                {
                    int suffix = System.Random.Shared.Next(0, 0x10000);
                    _sessionId = System.DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                               + "-"
                               + suffix.ToString("x4", System.Globalization.CultureInfo.InvariantCulture);
                }
                return _sessionId;
            }
        }
    }

    // The self-describing format embeds payload names and types in the
    // event itself, so the dispatcher rebuilds any provider's payload
    // without a manifest or per-provider code.
    protected DeckleEventSource()
        : base(EventSourceSettings.EtwSelfDescribingEventFormat)
    {
    }
}
