using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Abstract base for every Deckle.* EventSource. Each emitting module
// derives one concrete provider (DeckleAudioSource, DeckleWhispSource,
// …) and declares its [Event(...)] methods directly. This class
// carries the things that must be uniform across providers — the
// session id stamped on every event, a IsEnabled fast-path helper, and
// a couple of conventions that any consumer (EventListener or
// dotnet-trace user) can rely on.
//
// Why a class rather than a static helper. EventSource is itself a
// class with one instance per provider, and the BCL pattern is to
// derive from it for each provider. Holding the shared session id at
// this layer is the only place that survives all subclasses without
// duplication.
//
// Why no Log() catch-all method. The doctrine is strict-typed: every
// distinct call site becomes its own [Event(...)] method on the
// concrete provider. A generic Log(string, EventLevel) here would be
// the escape hatch that the legacy LogService offered, and it would
// rot the discipline. If a trivial event needs no payload, the
// concrete provider exposes it as a parameterless [Event] method
// (e.g. `WarmingUp()`); that stays typed.
//
// Session id. Generated lazily on first read (cheap, lock-free after
// init) and shared by every provider derived from this class. The
// format is "YYYY-MM-DD-XXXX" with XXXX a 4-hex random suffix so
// benchmark tooling can group rows by process session.
public abstract class DeckleEventSource : EventSource
{
    private static string? _sessionId;
    private static readonly object _sessionLock = new();

    // Process-local session id. Stays identical across every provider
    // for the life of the process. Consumers that need to stamp the
    // session on a JSONL row read this property — no need to pipe it
    // through every event signature.
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

    // Subclasses pass settings: EventSourceSettings.EtwSelfDescribingEventFormat.
    // The self-describing format embeds payload names and types in the
    // event itself, so a generic EventListener can rehydrate the
    // payload without a manifest. That's what lets the JSONL listener
    // serialise events from any provider without per-provider code.
    protected DeckleEventSource()
        : base(EventSourceSettings.EtwSelfDescribingEventFormat)
    {
    }
}
