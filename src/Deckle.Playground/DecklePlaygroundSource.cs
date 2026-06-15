using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Playground;

// Playground module provider. Covers the Playground shell (navigation), tuning
// pages (HomePage / HudPage / AmbientPage) with their many diagnostic
// interactions, the AmbientViewModel (HDR tone-mapping sliders + multi-light
// switch), and the host PlaygroundWindow itself.
//
// Provider Name = "Deckle-Playground" → [PLAYGROUND] tag through the legacy
// bridge.
//
// Verbose/Info separation per Deckle.Diagnostics/CLAUDE.md: an Info/Warning is a
// short Capital sentence with no IDs and no k=v; the technical detail (handles,
// reasons, ids, exceptions) lives in a Verbose mirror that FOLLOWS it. The many
// diagnostic interactions (Hue bridge calls, entertainment-area matching, zone
// suggestion/assignment) are typed per operation — a failed Hue call collapses
// to one milestone + one Verbose detail keyed by the operation name rather than
// a free-form string channel.
[EventSource(Name = "Deckle-Playground")]
public sealed class DecklePlaygroundSource : DeckleEventSource
{
    public static readonly DecklePlaygroundSource Log = new();

    // Single nav-start clock for the PlaygroundWindow — same role as the
    // SettingsWindow one. Restarted in OnNavSelectionChanged at nav-start;
    // feeds NavTiming (Navigate-return) and the page's first-Loaded PageReady,
    // so both measures share ONE timestamp. Static: the page Loaded handlers
    // (HudPage/AmbientPage/SegmentationPage) see the provider, not the window.
    public static readonly Stopwatch NavClock = new();

    private DecklePlaygroundSource() { }

    // ── Event IDs ─────────────────────────────────────────────────────────────
    // Milestones keep their original id; the Verbose mirrors added for the typed
    // rewrite take fresh ids 12-22 at the end of the sequence. 4/10/11 — the old
    // generic SettingChangedDetail / AmbientVerbose / AmbientInfo channels split
    // 1:many into typed events with no single successor; burned, never reused.
    // IDs are public in the ETW manifest; never reuse an id after deleting an event.
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

    // ── Navigation (PlaygroundWindow) ───────────────────────────────────────

    [Event(EvtNavigationRejected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A navigation request was rejected")]
    public void NavigationRejected()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavigationRejected);
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
           Message = "Navigating to a page failed")]
    public void NavigationFailed()
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavigationFailed);
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

    // (a) Navigate-return duration on the success path (the Playground success
    // branch previously logged nothing). Mirrors whisper ModelLoadComplete:
    // measured ms, typed field, Verbose. From NavClock (set once per nav).
    [Event(EvtNavTiming,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "nav timing | page={0} | duration_ms={1}")]
    public void NavTiming(string page, long duration_ms)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNavTiming, page, duration_ms);
    }

    // (b) Time from nav-start (NavClock) to the page's first Loaded — captures
    // the heavy work (ViewModel.Load, BuildTuningPanel, Win2D mount) that
    // Navigate returns before. Verbose, ms.
    [Event(EvtPageReady,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page ready | page={0} | ready_ms={1}")]
    public void PageReady(string page, long ready_ms)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPageReady, page, ready_ms);
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
           Message = "Starting the screen capture failed")]
    public void ScreenCaptureStartFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtScreenCaptureStartFailed);
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
           Message = "A Hue bridge call failed")]
    public void HueCallFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Push))
            WriteEvent(EvtHueCallFailed);
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
