using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Hud;

// EventSource provider for the Deckle.Hud module. A module cannot depend on the
// host, so the module that emits owns its provider.
//
// Every Verbose event belongs to the Windowing activity: the HUD is a set of
// window surfaces, and its per-show and per-frame detail stays out of the
// log stream unless that verbosity control admits it. Warnings, Errors and
// recoveries always pass.
//
// The window show and placement incidents (a refused topmost raise, a refused
// MoveAndResize) are not this provider's: the HUD surfaces report them through
// `WindowingProbe`, into `DeckleWindowingSource`.
[EventSource(Name = "Deckle-Hud")]
public sealed class DeckleHudSource : DeckleEventSource
{
    public static readonly DeckleHudSource Log = new();

    // ── EventIds: never reused (public in the ETW manifest) ─────────────
    // 1 retired.
    public const int EvtStateChanged        = 2;
    public const int EvtFadeInStarted       = 3;
    // 4 retired.
    // 5 reserved: a removed event; never reuse the id.
    public const int EvtProximityRollup     = 6;
    // 7 retired.
    // 8 retired.
    // 9 reserved: a removed event; never reuse the id.
    public const int EvtRevealGeometry      = 10;
    // 11 retired.
    public const int EvtTranscriptPanelVisibilityChanged = 12;
    public const int EvtHideSyncTimedOut          = 13;
    public const int EvtRevealMaskDegraded        = 14;
    public const int EvtRevealBuildAttemptFailed  = 15;
    public const int EvtRevealMaskRecovered       = 16;
    public const int EvtRevealBuildEnded          = 17;
    public const int EvtStrokeCreationFailed      = 18;
    public const int EvtStrokeCreationRecovered   = 19;
    public const int EvtStrokeCreationAttemptFailed = 20;
    // 21 retired.
    // 22 retired.
    public const int EvtHudDisplaySuppressed      = 23;
    public const int EvtOverlayCardChanged        = 24;
    public const int EvtCleanupFailed             = 25;
    public const int EvtFadeInEnded               = 26;
    public const int EvtSettingsLoadWarning = 27;
    public const int EvtSettingsLoadError = 28;

    // Callers test this before any measurement made only for the log.
    [NonEvent]
    public bool IsHudDetailEnabled(EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Windowing, this, EventLevel.Verbose, keywords);

    // ─── Paste rendezvous ───────────────────────────────────────────────
    //
    // HideSync released the transcribe thread without the hide: the paste goes
    // ahead while the HUD may still be visible, so activation can move while
    // the keystrokes are in flight.
    [Event(EvtHideSyncTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The HUD could not hide before the paste, so focus may move while the text is pasted\nreason={0} | timeout_ms={1}")]
    public void HideSyncTimedOut(string reason, int timeout_ms)
    {
        if (IsEnabled()) WriteEvent(EvtHideSyncTimedOut, reason, timeout_ms);
    }

    // ─── Digit reveal mask ──────────────────────────────────────────────
    //
    // One incident across takes: the Warning opens once the build has failed
    // past the transient window, carrying the last cause; the recovery closes
    // it on the first take whose six masks build. The face shows the flat
    // accent meanwhile, so a degraded reveal isn't mistaken for a design
    // choice. Every `attempts` here counts the take's failed build attempts.
    [Event(EvtRevealMaskDegraded,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The digit reveal fell back to the flat accent\nattempts={0} | ex_type={1} | ex_message={2}")]
    public void RevealMaskDegraded(int attempts, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRevealMaskDegraded, attempts, ex_type, ex_message);
    }

    [Event(EvtRevealMaskRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The digit reveal builds again")]
    public void RevealMaskRecovered(int attempts)
    {
        if (IsEnabled()) WriteEvent(EvtRevealMaskRecovered, attempts);
    }

    // One per failed vsync build attempt, up to the latch.
    [Event(EvtRevealBuildAttemptFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "attempt={0} | ex_type={1} | ex_message={2}")]
    public void RevealBuildAttemptFailed(int attempt, string ex_type, string ex_message)
    {
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtRevealBuildAttemptFailed, attempt, ex_type, ex_message);
    }

    // The end of one take's build loop. `outcome`: built (six masks),
    // latched (the retry budget ran out, flat accent for the take) or
    // abandoned (the take left the reveal before any outcome).
    [Event(EvtRevealBuildEnded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "outcome={0} | attempts={1}")]
    public void RevealBuildEnded(string outcome, int attempts)
    {
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtRevealBuildEnded, outcome, attempts);
    }

    // The inputs of the reveal placement (conicCentre = apex − cellOffset), so
    // a misplaced digit reveal can be read straight off the log.
    [Event(EvtRevealGeometry,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "cell={0}x{1} | offset={2},{3} | host={4}x{5}")]
    public void RevealGeometry(double cellW, double cellH, double offX, double offY, double hostW, double hostH)
    {
        HotLoops.HudRevealGeometry.Tick();
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtRevealGeometry, cellW, cellH, offX, offY, hostW, hostH);
    }

    // ─── Processing stroke ──────────────────────────────────────────────
    //
    // Composition refused the stroke (device lost, surface creation,
    // expression parse); the exception still reaches the caller, which used to
    // be the only trace. One incident: every failure is Verbose, the first one
    // also opens the Warning, and the next successful attach or rebuild closes
    // it. `phase`: attach (a state change) or rebuild (a settings change).
    [Event(EvtStrokeCreationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Cannot build the HUD processing stroke\nphase={0} | variant={1} | ex_type={2} | ex_message={3}")]
    public void StrokeCreationFailed(string phase, string variant, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtStrokeCreationFailed, phase, variant, ex_type, ex_message);
    }

    [Event(EvtStrokeCreationAttemptFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "phase={0} | variant={1} | ex_type={2} | ex_message={3}")]
    public void StrokeCreationAttemptFailed(string phase, string variant, string ex_type, string ex_message)
    {
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtStrokeCreationAttemptFailed, phase, variant, ex_type, ex_message);
    }

    [Event(EvtStrokeCreationRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The HUD processing stroke builds again")]
    public void StrokeCreationRecovered(int failures)
    {
        if (IsEnabled()) WriteEvent(EvtStrokeCreationRecovered, failures);
    }

    // A visible state or a feedback card the Overlay.Enabled setting turned
    // away, so a feedback emitted upstream never reached the screen.
    // `surface`: hud, overlay or transcript-panel. `severity`: 0=Info,
    // 1=Warning, 2+=Error, -1 for a state without a message.
    [Event(EvtHudDisplaySuppressed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "surface={0} | state={1} | severity={2} | reason={3}")]
    public void HudDisplaySuppressed(string surface, string state, int severity, string reason)
    {
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtHudDisplaySuppressed, surface, state, severity, reason);
    }

    // `change`: shown or closed. `cards` counts the stack after the change.
    [Event(EvtOverlayCardChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "change={0} | slot={1} | severity={2} | cards={3}")]
    public void OverlayCardChanged(string change, int slot, int severity, int cards)
    {
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtOverlayCardChanged, change, slot, severity, cards);
    }

    // A teardown step that threw and was abandoned: the overlay card may stay on
    // screen with nothing left tracking it, so this is a Warning, not detail.
    // `what`: overlay-expiry, overlay-shutdown.
    [Event(EvtCleanupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An overlay card could not be closed and may stay on screen\nwhat={0} | ex_type={1} | ex_message={2}")]
    public void CleanupFailed(string what, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCleanupFailed, what, ex_type, ex_message);
    }

    // ─── State machine transitions ──────────────────────────────────────
    //
    // `alpha` and `dpi` are the window's parameters at the transition; a bad
    // alpha or unexpected dpi often points to a fade-in or DPI-aware resizing
    // bug.
    [Event(EvtStateChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "from={0} | to={1} | reason={2} | alpha={3} | dpi={4}")]
    public void StateChanged(string from, string to, string reason, byte alpha, int dpi)
    {
        HotLoops.HudStateChanged.Tick();
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtStateChanged, from, to, reason, alpha, dpi);
    }

    // ─── Live transcript panel visibility ──────────────────────────────
    //
    // Content-free by design: the side and the utterance count, never the
    // transcript text.
    [Event(EvtTranscriptPanelVisibilityChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "shown={0} | side={1} | utterances={2}")]
    public void TranscriptPanelVisibilityChanged(bool shown, string side, int utterances)
    {
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtTranscriptPanelVisibilityChanged, shown, side, utterances);
    }

    // ─── Fade-in ────────────────────────────────────────────────────────
    //
    // `scope` names the surface that owns the alpha animator: "hud" for
    // HudWindow, "overlay" for HudOverlayWindow.
    [Event(EvtFadeInStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "scope={0} | duration_ms={1} | from={2} | to={3}")]
    public void FadeInStarted(string scope, int duration_ms, byte from_alpha, byte to_alpha)
    {
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtFadeInStarted, scope, duration_ms, from_alpha, to_alpha);
    }

    // The end of every FadeInStarted. `outcome`: completed (the ramp ended,
    // or snapped to its target when animations were turned off) or cancelled
    // (a hide, a fade-out or a new fade-in replaced it). `alpha` is the alpha
    // the window holds at that moment.
    [Event(EvtFadeInEnded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "scope={0} | outcome={1} | elapsed_ms={2} | alpha={3}")]
    public void FadeInEnded(string scope, string outcome, int elapsed_ms, byte alpha)
    {
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtFadeInEnded, scope, outcome, elapsed_ms, alpha);
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
           Message = "duration_ms={0} | samples={1} | min_alpha={2} | max_alpha={3} | p50_cursor_dist_dip={4} | p95_cursor_dist_dip={5}")]
    public void ProximityRollup(int duration_ms, int samples, byte min_alpha, byte max_alpha, int p50_cursor_dist_dip, int p95_cursor_dist_dip)
    {
        if (!IsHudDetailEnabled((EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtProximityRollup, duration_ms, samples, min_alpha, max_alpha, p50_cursor_dist_dip, p95_cursor_dist_dip);
    }

    // The settings store reports in free text; the text carries its own cause.
    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The HUD settings store worked around a problem\ndetail={0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The HUD settings could not be saved or applied\ndetail={0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }
}
