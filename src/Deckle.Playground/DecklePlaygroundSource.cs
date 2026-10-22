using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Playground;

// Playground provider: window navigation and page timing, plus the Ambient
// tab's capture, Hue and zone interactions and the HUD tab's microphone
// preview. The AmbientViewModel the Ambient tab binds logs its own edits on
// Deckle-Ambient.
[EventSource(Name = "Deckle-Playground")]
public sealed class DecklePlaygroundSource : DeckleEventSource
{
    public static readonly DecklePlaygroundSource Log = new();

    // Restarted once per navigation, so NavTiming and the page's PageReady
    // share one timestamp. Static so the page Loaded handlers reach it
    // without the window.
    public static readonly Stopwatch NavClock = new();

    private DecklePlaygroundSource() { }

    // ── Event IDs ─────────────────────────────────────────────────────────────
    // 4, 10 and 11 are retired. IDs are public in the ETW manifest; never
    // reuse an id after deleting an event.
    public const int EvtNavigationRejected           = 1;
    public const int EvtNavigationFailed             = 2;
    public const int EvtTuningChanged                = 3;
    public const int EvtScreenCaptureToggled         = 5;
    public const int EvtScreenCaptureStartFailed     = 6;
    public const int EvtHueCallFailed                = 7;
    public const int EvtEntertainmentAreaUsed        = 8;
    public const int EvtHueCallFailedDetail          = 9;
    public const int EvtNavigationRejectedDetail     = 12;
    public const int EvtNavigationFailedDetail       = 13;
    public const int EvtScreenCaptureStartFailedDetail = 14;
    public const int EvtEntertainmentAreaUsedDetail  = 15;
    public const int EvtPipelineModeChanged          = 16;
    public const int EvtPipelineModeChangedDetail    = 17;
    public const int EvtLightZoneUpdated             = 18;
    public const int EvtLightZoneUpdatedDetail       = 19;
    public const int EvtResolveLights                = 20;
    public const int EvtMatchEntertainmentArea       = 21;
    public const int EvtZoneSuggested                = 22;
    // ── Page navigation timing (structured-verbose, ms) ──
    public const int EvtNavTiming                    = 23;
    public const int EvtPageReady                    = 24;
    // ── HUD tab microphone preview ──
    public const int EvtMicrophonePreviewFellBack    = 25;

    // ── Navigation (PlaygroundWindow) ───────────────────────────────────────

    [Event(EvtNavigationRejected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A navigation request was rejected | reason={0}")]
    public void NavigationRejected(string reason)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavigationRejected, reason);
    }

    [Event(EvtNavigationRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "nav rejected | reason={0} | item={1}")]
    public void NavigationRejectedDetail(string reason, string item)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavigationRejectedDetail, reason, item);
    }

    [Event(EvtNavigationFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Navigating to a page failed | page={0} | reason={1}")]
    public void NavigationFailed(string page, string reason)
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavigationFailed, page, reason);
    }

    [Event(EvtNavigationFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "nav failed | page={0} | reason={1} | error={2}")]
    public void NavigationFailedDetail(string page, string reason, string error)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavigationFailedDetail, page, reason, error);
    }

    // Navigate-return duration on the success path, from NavClock.
    [Event(EvtNavTiming,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "nav timing | page={0} | duration_ms={1}")]
    public void NavTiming(string page, long duration_ms)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavTiming, page, duration_ms);
    }

    // From nav-start to the page's Loaded: captures the heavy work
    // (ViewModel.Load, BuildTuningPanel, Win2D mount) Navigate returns before.
    [Event(EvtPageReady,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page ready | page={0} | ready_ms={1}")]
    public void PageReady(string page, long ready_ms)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPageReady, page, ready_ms);
    }

    // ── HUD tab: live microphone preview ────────────────────────────────────

    // The HUD tab asked for the microphone and got the simulation instead. The
    // capture's own failure events (Deckle-Audio, tagged purpose=playground)
    // carry the device detail; this one names what was asked and what was
    // obtained, so a reader of the journal does not take it for a dictation
    // that failed. cause: in-use, not-detected, unavailable or exception.
    // mmsys_err is 0 and ex_type empty when the cause does not carry them.
    [Event(EvtMicrophonePreviewFellBack,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The HUD preview fell back from the microphone to the simulation | cause={0} | mmsys_err={1} | ex_type={2}")]
    public void MicrophonePreviewFellBack(string cause, uint mmsys_err, string ex_type)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtMicrophonePreviewFellBack, cause, mmsys_err, ex_type);
    }

    // ── ViewModel setters (tuning sliders) ──────────────────────────────────

    [Event(EvtTuningChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "tuning changed | setting={0} | value={1}")]
    public void TuningChanged(string setting, string value)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtTuningChanged, setting, value);
    }

    // ── Ambient pipeline mode (multi-light switch) ──────────────────────────

    [Event(EvtPipelineModeChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The ambient pipeline mode changed")]
    public void PipelineModeChanged()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtPipelineModeChanged);
    }

    [Event(EvtPipelineModeChangedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "pipeline mode | mode={0}")]
    public void PipelineModeChangedDetail(string mode)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtPipelineModeChangedDetail, mode);
    }

    // ── Screen capture page ─────────────────────────────────────────────────

    [Event(EvtScreenCaptureToggled,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "playground toggle | running={0}")]
    public void ScreenCaptureToggled(bool running)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtScreenCaptureToggled, running);
    }

    [Event(EvtScreenCaptureStartFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Starting the screen capture failed | ex_type={0}")]
    public void ScreenCaptureStartFailed(string ex_type)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtScreenCaptureStartFailed, ex_type);
    }

    [Event(EvtScreenCaptureStartFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "screen capture start | error={0}")]
    public void ScreenCaptureStartFailedDetail(string error)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtScreenCaptureStartFailedDetail, error);
    }

    // ── Hue interactions ────────────────────────────────────────────────────

    [Event(EvtHueCallFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A Hue bridge call failed | op={0} | ex_type={1}")]
    public void HueCallFailed(string op, string ex_type)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Push))
            WriteEvent(EvtHueCallFailed, op, ex_type);
    }

    [Event(EvtHueCallFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "hue call failed | op={0} | error={1}")]
    public void HueCallFailedDetail(string op, string error)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push))
            WriteEvent(EvtHueCallFailedDetail, op, error);
    }

    [Event(EvtEntertainmentAreaUsed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Using an entertainment area as the lights source")]
    public void EntertainmentAreaUsed()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Push))
            WriteEvent(EvtEntertainmentAreaUsed);
    }

    [Event(EvtEntertainmentAreaUsedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "ent area source | name={0} | lights={1}")]
    public void EntertainmentAreaUsedDetail(string name, int lights)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push))
            WriteEvent(EvtEntertainmentAreaUsedDetail, name, lights);
    }

    // ── Ambient zone resolution ─────────────────────────────────────────────

    [Event(EvtResolveLights,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "resolve lights | group_id={0} | group_name={1} | from_group={2}")]
    public void ResolveLights(string group_id, string group_name, int from_group)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtResolveLights, group_id, group_name, from_group);
    }

    [Event(EvtMatchEntertainmentArea,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "match ent area | result={0} | ent_id={1} | name={2} | overlap={3}")]
    public void MatchEntertainmentArea(string result, string ent_id, string name, int overlap)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtMatchEntertainmentArea, result, ent_id, name, overlap);
    }

    [Event(EvtZoneSuggested,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "zone suggest | id={0} | zone={1} | ent_name={2} | x={3:F2} | y={4:F2} | z={5:F2}")]
    public void ZoneSuggested(string id, string zone, string ent_name, double x, double y, double z)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtZoneSuggested, id, zone, ent_name, x, y, z);
    }

    [Event(EvtLightZoneUpdated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A light zone was updated")]
    public void LightZoneUpdated()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtLightZoneUpdated);
    }

    [Event(EvtLightZoneUpdatedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "zone assign | id={0} | zone={1}")]
    public void LightZoneUpdatedDetail(string id, string zone)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtLightZoneUpdatedDetail, id, zone);
    }
}
