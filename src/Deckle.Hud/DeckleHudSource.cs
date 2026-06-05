using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Hud;

// EventSource provider for the Deckle.Hud module.
//
// Comes from the resolution of the obs/cartography conflict: obs had
// centralized HUD observations in DeckleAppSource (on the Deckle host side),
// but cartography extracted Deckle.Hud into a separate module, and a module
// cannot depend on the host. Modular doctrine (one provider per coherent
// component) requires an emitting module to own its provider; this file does
// that, following the pattern of the other Deckle.* providers.
//
// Initially the provider only carried the HideSync rendezvous timeout warning.
// The cross-cutting observability instrumentation wave (May 2026) extends it
// with four internal observation axes: state machine transitions, fade-in,
// message retract, and proximity rollup. This makes the HUD mechanics
// diagnosable from the LogWindow and JSONL files instead of through ad hoc
// File.AppendAllText calls. See `reference--eventsource-convention--1.2.md`
// §*Under-instrumented internal HUD* (gap 1.1), which motivates the extension,
// and the module CLAUDE.md §*Internal instrumentation* for the wiring doctrine.
[EventSource(Name = "Deckle-Hud")]
public sealed class DeckleHudSource : DeckleEventSource
{
    public static readonly DeckleHudSource Log = new();

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtHudWarning          = 1;
    public const int EvtStateChanged        = 2;
    public const int EvtFadeInStarted       = 3;
    public const int EvtMessageRetracted    = 4;
    // 5 reserved: former HUD composition warm pass event, removed in 2026-06
    // when boot-time PrimeAndHide was deleted.
    public const int EvtProximityRollup     = 6;

    [Event(EvtHudWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void HudWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHudWarning, message);
    }

    // ─── Axis 1 — Six-state state machine transitions ──────────────────
    //
    // Emitted by HudWindow.SetState on each transition (Hidden, Charging,
    // Recording, Transcribing, Rewriting, Message). `reason` captures the
    // caller-side semantic trigger (hotkey, paste, message_hide, etc.).
    // `alpha` and `dpi` are the window manager's technical parameters at the
    // time of the transition; a bad alpha or unexpected dpi often points to a
    // fade-in or DPI-aware resizing bug.
    [Event(EvtStateChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "state changed | from={0} | to={1} | reason={2} | alpha={3} | dpi={4}")]
    public void StateChanged(string from, string to, string reason, byte alpha, int dpi)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtStateChanged, from, to, reason, alpha, dpi);
    }

    // ─── Axis 2 — 150 ms cubic ease-out fade-in ────────────────────────
    //
    // Emitted at the start of each fade-in. `scope` distinguishes surfaces
    // with their own alpha animator: "hud" for HudWindow (raw input
    // proximity), "overlay" for HudOverlayWindow (60 Hz polling). A future
    // separate "message" surface (the retract hybrid bleed described in
    // CLAUDE.md but not implemented) would be added here.
    [Event(EvtFadeInStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "fade in start | scope={0} | duration_ms={1} | from={2} | to={3}")]
    public void FadeInStarted(string scope, int duration_ms, byte from_alpha, byte to_alpha)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtFadeInStarted, scope, duration_ms, from_alpha, to_alpha);
    }

    // ─── Axis 3 — Message retract 400×160 → 272×78 ─────────────────────
    //
    // Emitted at the start of the retract (hybrid bleed → standalone card).
    // There is no active call site in the current code: the retract mechanics
    // are described in CLAUDE.md as the target architecture, but HudMessage is
    // currently fixed at 272×78. The event is declared to freeze the signature;
    // it will activate when the retract mechanics are wired.
    [Event(EvtMessageRetracted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "message retract | from={0}x{1} | to={2}x{3} | duration_ms={4}")]
    public void MessageRetracted(int from_w, int from_h, int to_w, int to_h, int duration_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtMessageRetracted, from_w, from_h, to_w, to_h, duration_ms);
    }

    // ─── Axis 4 — Proximity smoothstep (per-session rollup) ────────────
    //
    // Canonical rollup pattern (see observable class #3 "High-frequency
    // real-time loop" in `reference--eventsource-convention--1.2.md`
    // §*Canonical observable classes*): proximity is evaluated at ~125 Hz on
    // WM_INPUT, a frequency too hot for the LogWindow according to the
    // "heartbeats < 1 s are not logged" doctrine. HudWindow accumulates over
    // the full visibility window (shown → hidden) and emits a single summary
    // when it becomes hidden, under two cumulative conditions: at least one
    // sample collected AND min_alpha != max_alpha (otherwise the mouse did not
    // enter the proximity radius, smoothstep stayed flat, and there is no
    // diagnostic material). A previous 1 s periodic variant flooded the
    // LogWindow with valueless events on sessions where nothing moved. The
    // strict gate avoids all allocation when no listener is attached,
    // including collection-side allocation (see _proximityRollupEnabled in
    // HudWindow). `duration_ms` is the actual visibility session duration, not
    // a fixed period.
    [Event(EvtProximityRollup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "proximity rollup | duration_ms={0} | samples={1} | min_alpha={2} | max_alpha={3} | p50_cursor_dist_dip={4} | p95_cursor_dist_dip={5}")]
    public void ProximityRollup(int duration_ms, int samples, byte min_alpha, byte max_alpha, int p50_cursor_dist_dip, int p95_cursor_dist_dip)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtProximityRollup, duration_ms, samples, min_alpha, max_alpha, p50_cursor_dist_dip, p95_cursor_dist_dip);
    }
}
