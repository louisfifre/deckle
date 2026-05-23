namespace Deckle.Diagnostics;

// Transverse keyword bits shared by every Deckle.* EventSource. Each
// provider OR-combines these with its own module-specific bits when
// emitting an event, so an EventListener (or `dotnet-trace`) can filter
// by domain across modules without knowing each provider's local
// taxonomy. Bits 0..4 are reserved here; bits 5..63 belong to the
// individual providers and stay local to each module's EventSource.
//
// The flags are [Flags] with explicit hexadecimal values so a future
// reader sees the bit layout at a glance — ETW keywords are a 64-bit
// bitmask and a missing bit is silently OK, so the layout is the
// contract.
[System.Flags]
public enum Keywords : long
{
    None      = 0,
    Lifecycle = 0x1,   // bit 0 — app/process boot, ready, quit, restart
    Capture   = 0x2,   // bit 1 — sources that feed a pipeline (mic, screen, hotkey)
    Pipeline  = 0x4,   // bit 2 — staged transformations (VAD, transcribe, rewrite, sampler)
    Push      = 0x8,   // bit 3 — outputs sent to the world (clipboard, paste, Hue, HUD)
    Heartbeat = 0x10,  // bit 4 — periodic/aggregated runtime telemetry
    // Bits 0x20+ are reserved for per-provider use.
}
