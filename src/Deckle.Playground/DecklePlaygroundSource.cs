using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Playground;

// Playground module provider. Covers the Playground shell (navigation), tuning
// pages (HomePage / HudPage / AmbientPage) with their many diagnostic
// interactions, the AmbientViewModel (HDR tone-mapping sliders + multi-light
// switch), and the host PlaygroundWindow itself.
//
// Provider Name = "Deckle-Playground" → [PLAYGROUND] tag through the legacy
// bridge. Playground is a dev/tuning surface: strict-typed doctrine is applied
// to clear transitions (nav, settings), but a generic channel is accepted for
// the many diagnostic interaction strings (Hue pairing attempts, zone scan,
// match entertainment areas) that are not each worth a typed event.
[EventSource(Name = "Deckle-Playground")]
public sealed class DecklePlaygroundSource : DeckleEventSource
{
    public static readonly DecklePlaygroundSource Log = new();

    private DecklePlaygroundSource() { }

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtNavWarning             = 1;
    public const int EvtNavError               = 2;
    public const int EvtSettingChanged         = 3;
    public const int EvtSettingChangedDetail   = 4;
    public const int EvtScreenCaptureVerbose   = 5;
    public const int EvtScreenCaptureWarning   = 6;
    public const int EvtHueWarning             = 7;
    public const int EvtHueInfo                = 8;
    public const int EvtHueVerbose             = 9;
    public const int EvtAmbientVerbose         = 10;
    public const int EvtAmbientInfo            = 11;

    // ── Navigation (PlaygroundWindow) ───────────────────────────────────

    [Event(EvtNavWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void NavWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtNavWarning, message);
    }

    [Event(EvtNavError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void NavError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtNavError, message);
    }

    // ── ViewModel setters ───────────────────────────────────────────────

    [Event(EvtSettingChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0} ← {1}")]
    public void SettingChanged(string property, string value)
    {
        if (IsEnabled()) WriteEvent(EvtSettingChanged, property, value);
    }

    [Event(EvtSettingChangedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingChangedDetail(string detail)
    {
        if (IsEnabled()) WriteEvent(EvtSettingChangedDetail, detail);
    }

    // ── Screen capture page ─────────────────────────────────────────────

    [Event(EvtScreenCaptureVerbose,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "{0}")]
    public void ScreenCaptureVerbose(string message)
    {
        if (IsEnabled()) WriteEvent(EvtScreenCaptureVerbose, message);
    }

    [Event(EvtScreenCaptureWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "{0}")]
    public void ScreenCaptureWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtScreenCaptureWarning, message);
    }

    // ── Hue interactions ────────────────────────────────────────────────

    [Event(EvtHueWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{0}")]
    public void HueWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHueWarning, message);
    }

    [Event(EvtHueInfo,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{0}")]
    public void HueInfo(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHueInfo, message);
    }

    [Event(EvtHueVerbose,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{0}")]
    public void HueVerbose(string message)
    {
        if (IsEnabled()) WriteEvent(EvtHueVerbose, message);
    }

    // ── Ambient interactions ────────────────────────────────────────────

    [Event(EvtAmbientVerbose,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}")]
    public void AmbientVerbose(string message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientVerbose, message);
    }

    [Event(EvtAmbientInfo,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}")]
    public void AmbientInfo(string message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientInfo, message);
    }
}
