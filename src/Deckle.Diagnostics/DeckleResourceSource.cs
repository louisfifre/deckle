using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: lifecycle of unmanaged native resources (D3D11
// textures on the Vision side, Composition visuals on the HUD side). Capturing
// acquire / release in one schema allows correlating a GPU leak or OOM crash
// with the last traced acquisition instead of blindly walking back through a
// native stack: an acquire without its release is the leak. The primitive is
// strictly non-business and consumed by several modules with the same
// parameter set.
//
// No leak-suspect event: every owner releases deterministically (no
// finalizer, no watchdog), so nothing could ever detect a missed release
// in-process. Ids 3 and 4 held that never-emitted pair.
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
//   "frame-sampler", "hud-chrono-stroke"). Differentiates two acquires of
//   the same kind on distinct sites without inflating the schema.
//
// size_bytes convention:
//   Approximate memory size. For textures: w * h * bytes_per_pixel.
//   For Composition visuals: 0 (impossible to measure from managed code
//   without costly introspection). For duplication output: 0 (pure handle).
[EventSource(Name = "Deckle-Resource")]
public sealed class DeckleResourceSource : DeckleEventSource
{
    public static readonly DeckleResourceSource Log = new();

    private DeckleResourceSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest; never reuse an id after deleting an event.
    public const int EvtResourceAcquired        = 1;
    public const int EvtResourceReleased        = 2;
    // 3 retired.
    // 4 retired.

    // Acquire: emitted when taking a native handle or creating a managed
    // Composition object. Verbose because it carries an opaque identifier (hex
    // handle) and follows the capture loop's cadence.
    [Event(EvtResourceAcquired,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Resource,
           Message = "kind={0} | handle=0x{1:X} | size_bytes={2} | owner={3}")]
    public void ResourceAcquired(string kind, long handle, int size_bytes, string owner)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Resource)) return;
        WriteEvent(EvtResourceAcquired, kind, handle, size_bytes, owner);
    }

    // Release: `age_ms` is the acquire-to-release delta, measured by the call
    // site.
    [Event(EvtResourceReleased,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Resource,
           Message = "kind={0} | handle=0x{1:X} | age_ms={2} | owner={3}")]
    public void ResourceReleased(string kind, long handle, int age_ms, string owner)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Resource)) return;
        WriteEvent(EvtResourceReleased, kind, handle, age_ms, owner);
    }

}
