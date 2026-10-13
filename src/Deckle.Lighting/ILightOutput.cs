namespace Deckle.Lighting;

// Driver-facing abstraction for the "downstream" half of the ambient
// lighting pipeline: connect to the sink the user picked, push one sRGB
// colour at the cadence the consumer chooses, disconnect cleanly. Every
// driver supports the single-colour push. Drivers that can address
// individual lights within their connected scope also implement
// <see cref="IMultiLightOutput"/> ; consumers query for the capability
// with a runtime cast and fall back to the single-colour push.
//
// Lifecycle. Construction is cheap and never touches the network;
// ConnectAsync must succeed before any push; DisposeAsync is idempotent.
//
// Threading. Methods are not required to be thread-safe ; the
// consumer serialises calls. Drivers that run background work own its
// synchronisation internally.
public interface ILightOutput : IAsyncDisposable
{
    /// <summary>True once <see cref="ConnectAsync"/> has completed
    /// successfully and the driver is ready to accept colour pushes.</summary>
    bool IsConnected { get; }

    /// <summary>True when state-change events observed from the sink
    /// can be attributed back to this output's own writes. Consumers
    /// use this to decide whether bridge-side events are useful for
    /// external-change detection during the active session.</summary>
    bool UsesStateEventAttribution => false;

    /// <summary>True when the connected output is a live stream that
    /// must keep receiving colour updates even when the desired colour
    /// has not changed. Consumers use this to bypass their delta gate
    /// while keeping persistent-state outputs such as REST throttled.</summary>
    bool RequiresContinuousColorUpdates => false;

    /// <summary>Driver-recommended colour update rate for a live output,
    /// in updates per second. Null leaves cadence selection to the consumer.
    /// This is a transport capability, not a user preference: persistent
    /// state outputs such as REST normally leave it unspecified, while a
    /// streaming transport can advertise the rate needed for smoothness and
    /// packet-loss tolerance.</summary>
    int? PreferredColorUpdateRateHz => null;

    /// <summary>Open the session with the configured sink. Throws if
    /// the sink is unreachable, the credentials are rejected, or any
    /// protocol-level handshake fails. Idempotent — calling twice on
    /// an already-connected driver is a no-op.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Pushes one sRGB colour to the whole connected scope
    /// (group, zone, universe). The caller paces the cadence.</summary>
    Task SetColorAsync(LightColor color, CancellationToken ct = default);
}

// Capability sub-interface : exposed by drivers that can address
// individual lights independently within the connected scope. AmbientEngine
// queries with `output is IMultiLightOutput` at start ; if yes, the
// multi-light pipeline runs, otherwise it falls back to the
// <see cref="ILightOutput.SetColorAsync"/> path with a single average
// colour.
//
// A separate interface rather than an optional method: a driver that
// cannot address individual fixtures (one DMX universe driving one
// parallel-wired strip) would otherwise lie with a no-op or throw
// NotSupportedException at runtime.
public interface IMultiLightOutput : ILightOutput
{
    /// <summary>Lists the lights addressable within the connected scope,
    /// in the driver's own order. Returns an empty list when the driver
    /// is connected but the underlying scope has no lights. Throws
    /// <see cref="InvalidOperationException"/> when called before
    /// <see cref="ILightOutput.ConnectAsync"/> has completed. Drivers
    /// MAY cache the result for the lifetime of the connection ; callers
    /// that need fresh data should reconnect.</summary>
    Task<IReadOnlyList<LightDescriptor>> ListLightsAsync(CancellationToken ct = default);

    /// <summary>Pushes one sRGB colour per light in a single logical
    /// batch. Drivers are free to parallelise the underlying transport
    /// calls. Light ids not present in <see cref="ListLightsAsync"/> are
    /// ignored silently: the consumer may hold stale placements.</summary>
    Task SetLightColorsAsync(IReadOnlyDictionary<string, LightColor> colorsByLightId, CancellationToken ct = default);

    /// <summary>Makes the light briefly identifiable so the user can spot
    /// it, typically a flash. Returns once the driver accepted the
    /// instruction; the flash may outlast the Task (Hue's <c>lselect</c>
    /// runs about 15 s). Drivers with no equivalent may no-op.</summary>
    Task IdentifyLightAsync(string lightId, CancellationToken ct = default);

    /// <summary>Cuts an identify flash short. Harmless on a light that is
    /// not flashing.</summary>
    Task StopIdentifyAsync(string lightId, CancellationToken ct = default);
}
