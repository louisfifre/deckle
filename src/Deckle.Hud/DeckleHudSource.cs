using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Hud;

// EventSource provider for the Deckle.Hud module. A module cannot depend on the
// host, so the module that emits owns its provider.
[EventSource(Name = "Deckle-Hud")]
public sealed class DeckleHudSource : DeckleEventSource
{
    public static readonly DeckleHudSource Log = new();

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtHudWarning          = 1;
    public const int EvtStateChanged        = 2;
    public const int EvtFadeInStarted       = 3;
    public const int EvtMessageRetracted    = 4;
    // 5 reserved: a removed event; never reuse the id.
    public const int EvtProximityRollup     = 6;
    public const int EvtHudWarningDetail    = 7;
    public const int EvtRevealMaskFailed    = 8;
    // 9 reserved: a removed event; never reuse the id.
    public const int EvtRevealGeometry      = 10;
    public const int EvtRevealMaskFailedDetail = 11;
    public const int EvtTranscriptPanelVisibilityChanged = 12;

    [Event(EvtHudWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "HideSync timed out")]
    public void HudWarning()
    {
        if (IsEnabled()) WriteEvent(EvtHudWarning);
    }

    [Event(EvtHudWarningDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void HudWarningDetail(string message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtHudWarningDetail, message);
    }

    // The digit reveal mask kept failing to build, so the face fell back to the
    // flat-accent overlay; this records it so a degraded reveal isn't mistaken
    // for a design choice.
    [Event(EvtRevealMaskFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Digit reveal mask failed")]
    public void RevealMaskFailed()
    {
        if (IsEnabled()) WriteEvent(EvtRevealMaskFailed);
    }

    [Event(EvtRevealMaskFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "digit reveal mask failed | error={0} | message={1}")]
    public void RevealMaskFailedDetail(string error, string message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtRevealMaskFailedDetail, error, message);
    }

    // The inputs of the reveal placement (conicCentre = apex − cellOffset), so
    // a misplaced digit reveal can be read straight off the log.
    [Event(EvtRevealGeometry,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reveal geometry | cell={0}x{1} | offset={2},{3} | host={4}x{5}")]
    public void RevealGeometry(double cellW, double cellH, double offX, double offY, double hostW, double hostH)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtRevealGeometry, cellW, cellH, offX, offY, hostW, hostH);
    }

    // ─── State machine transitions ──────────────────────────────────────
    //
    // `alpha` and `dpi` are the window's parameters at the transition; a bad
    // alpha or unexpected dpi often points to a fade-in or DPI-aware resizing
    // bug.
    [Event(EvtStateChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "state changed | from={0} | to={1} | reason={2} | alpha={3} | dpi={4}")]
    public void StateChanged(string from, string to, string reason, byte alpha, int dpi)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtStateChanged, from, to, reason, alpha, dpi);
    }

    // ─── Live transcript panel visibility ──────────────────────────────
    //
    // Content-free by design: the side and the utterance count, never the
    // transcript text.
    [Event(EvtTranscriptPanelVisibilityChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "transcript panel | shown={0} | side={1} | utterances={2}")]
    public void TranscriptPanelVisibilityChanged(bool shown, string side, int utterances)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtTranscriptPanelVisibilityChanged, shown, side, utterances);
    }

    // ─── Fade-in ────────────────────────────────────────────────────────
    //
    // `scope` names the surface that owns the alpha animator: "hud" for
    // HudWindow, "overlay" for HudOverlayWindow.
    [Event(EvtFadeInStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "fade in start | scope={0} | duration_ms={1} | from={2} | to={3}")]
    public void FadeInStarted(string scope, int duration_ms, byte from_alpha, byte to_alpha)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtFadeInStarted, scope, duration_ms, from_alpha, to_alpha);
    }

    // ─── Message retract ────────────────────────────────────────────────
    //
    // No call site: the event only freezes the signature of a planned message
    // retract; HudMessage is fixed at 272×78.
    [Event(EvtMessageRetracted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "message retract | from={0}x{1} | to={2}x{3} | duration_ms={4}")]
    public void MessageRetracted(int from_w, int from_h, int to_w, int to_h, int duration_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtMessageRetracted, from_w, from_h, to_w, to_h, duration_ms);
    }

    // ─── Proximity smoothstep (per-session rollup) ──────────────────────
    //
    // Proximity runs on every cursor move, too hot for the log: heartbeats
    // under one second are never logged. HudWindow aggregates one visibility
    // (shown → hidden) into this single summary. `duration_ms` is the actual
    // visibility duration, not a fixed period.
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
