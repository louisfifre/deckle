using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Playground;

// Playground module provider. Covers the Playground shell (navigation), page
// readiness timing, and the host PlaygroundWindow itself.
//
// Provider Name = "Deckle-Playground" → [PLAYGROUND] tag through the legacy
// bridge.
//
// Verbose/Info separation per Deckle.Diagnostics/CLAUDE.md: an Info/Warning is a
// short Capital sentence with no IDs and no k=v; the technical detail (handles,
// reasons, ids, exceptions) lives in a Verbose mirror that FOLLOWS it.
[EventSource(Name = "Deckle-Playground")]
public sealed class DecklePlaygroundSource : DeckleEventSource
{
    public static readonly DecklePlaygroundSource Log = new();

    // Single nav-start clock for the PlaygroundWindow — same role as the
    // SettingsWindow one. Restarted in OnNavSelectionChanged at nav-start;
    // feeds NavTiming (Navigate-return) and the page's first-Loaded PageReady,
    // so both measures share ONE timestamp. Static: the page Loaded handlers
    // (HudPage/SegmentationPage) see the provider, not the window.
    public static readonly Stopwatch NavClock = new();

    private DecklePlaygroundSource() { }

    // ── Event IDs ─────────────────────────────────────────────────────────────
    // Milestones keep their original id; the Verbose mirrors added for the typed
    // rewrite take fresh ids 12-22 at the end of the sequence. 4/10/11 — the old
    // generic SettingChangedDetail / AmbientVerbose / AmbientInfo channels split
    // 1:many into typed events with no single successor; burned, never reused.
    // IDs are public in the ETW manifest; never reuse an id after deleting an event.
    //
    // Retired with the Playground Ambient tab (2026-10-08), reserved, never
    // reused: 3 TuningChanged, 5 ScreenCaptureToggled, 6 ScreenCaptureStartFailed,
    // 7 HueCallFailed, 8 EntertainmentAreaUsed, 9 HueCallFailedDetail,
    // 14 ScreenCaptureStartFailedDetail, 15 EntertainmentAreaUsedDetail,
    // 16 PipelineModeChanged, 17 PipelineModeChangedDetail, 18 LightZoneUpdated,
    // 19 LightZoneUpdatedDetail, 20 ResolveLights, 21 MatchEntertainmentArea,
    // 22 ZoneSuggested. Their successors live on Deckle-Ambient (65-77).
    public const int EvtNavigationRejected           = 1;
    public const int EvtNavigationFailed             = 2;
    public const int EvtNavigationRejectedDetail     = 12;
    public const int EvtNavigationFailedDetail       = 13;
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
}
