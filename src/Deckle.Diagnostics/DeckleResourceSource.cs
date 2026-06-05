using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: lifecycle of unmanaged native resources (D3D11
// textures on the Vision side, Composition visuals on the HUD side, future
// native Whisper handles). Capturing acquire / release / leak in one schema
// allows correlating a GPU leak or OOM crash with the last traced acquisition
// instead of blindly walking back through a native stack. The primitive is
// strictly non-business and consumed by several modules with the same parameter
// set: promotion to cross-cutting sub-provider under the two-clause criterion
// in `reference--eventsource-convention--1.2.md`
// §*Cross-cutting sub-providers*.
//
// Closed `kind` vocabulary:
//   "d3d11-texture"       — ID3D11Texture2D (capture frames, sampler)
//   "duplication-output"  — IDXGIOutputDuplication
//   "dxgi-resource"       — generic IDXGIResource
//   "composition-visual"  — Microsoft.UI.Composition.Visual and derivatives
//   "composition-surface" — ICompositionSurface, CompositionDrawingSurface
//   "composition-brush"   — CompositionBrush and derivatives
// Any new kind must be added here before use to preserve listener-side
// grep-ability.
//
// Handle conventions:
//   - COM / native: IntPtr of the interface pointer, cast to long.
//   - Managed Composition: RuntimeHelpers.GetHashCode(obj), cast to long;
//     stable identifier for the lifetime of a given managed object, sufficient
//     to match an acquire and its release.
//
// Owner convention:
//   Short name of the logical site driving the resource ("capture-loop",
//   "frame-sampler", "hud-message", "hud-glow", etc.). Differentiates two
//   acquires of the same kind on distinct sites without inflating the schema.
//
// size_bytes convention:
//   Approximate memory size. For textures: w * h * bytes_per_pixel.
//   For Composition visuals: 0 (impossible to measure from managed code
//   without costly introspection). For duplication output: 0 (pure handle).
//
// The `ResourceLeakSuspect` event is declared to freeze the contract in this
// wave, but active wiring (missed release detection through finalizer or
// watchdog) will come in a later pass. No active call site in the current code.
[EventSource(Name = "Deckle-Resource")]
public sealed class DeckleResourceSource : DeckleEventSource
{
    public static readonly DeckleResourceSource Log = new();

    private DeckleResourceSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtResourceAcquired    = 1;
    public const int EvtResourceReleased    = 2;
    public const int EvtResourceLeakSuspect = 3;

    // Acquire: emitted when taking a native handle or creating a managed
    // Composition object. Verbose because it carries an opaque identifier (hex
    // handle) and cadence can be high (capture loop ~15 Hz).
    [Event(EvtResourceAcquired,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Resource,
           Message = "resource acquired | kind={0} | handle=0x{1:X} | size_bytes={2} | owner={3}")]
    public void ResourceAcquired(string kind, long handle, int size_bytes, string owner)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Resource)) return;
        WriteEvent(EvtResourceAcquired, kind, handle, size_bytes, owner);
    }

    // Release: emitted at Marshal.ReleaseComObject, Dispose, or equivalent
    // time. `age_ms` measures the delta between acquire and release through
    // Stopwatch.GetTimestamp, captured on the call-site side. Verbose for the
    // same reasons as acquire.
    [Event(EvtResourceReleased,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Resource,
           Message = "resource released | kind={0} | handle=0x{1:X} | age_ms={2} | owner={3}")]
    public void ResourceReleased(string kind, long handle, int age_ms, string owner)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Resource)) return;
        WriteEvent(EvtResourceReleased, kind, handle, age_ms, owner);
    }

    // Leak suspect: specialized event for the abnormal case (missed release
    // detected at finalization or by watchdog). Warning because this is an
    // anomaly that deserves surfacing even when Verbose is not listened to. No
    // active site today; declared to freeze the signature before detection is
    // wired.
    [Event(EvtResourceLeakSuspect,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Resource,
           Message = "resource leak suspect | kind={0} | handle=0x{1:X} | age_ms={2} | owner={3} | symptom={4}")]
    public void ResourceLeakSuspect(string kind, long handle, int age_ms, string owner, string symptom)
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Resource)) return;
        WriteEvent(EvtResourceLeakSuspect, kind, handle, age_ms, owner, symptom);
    }
}
