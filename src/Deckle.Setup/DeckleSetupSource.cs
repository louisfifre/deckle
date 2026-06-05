using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

// Setup wizard provider. Covers first-run wizard pages under
// src/Deckle/Shell/Setup/: ChoicesPage (selection of items to download),
// InstallingPage (download orchestration + verification), SummaryPage (final
// summary), SetupWindow (window lifecycle).
//
// Provider Name = "Deckle-Setup" → [SETUP] tag through the bridge. Legacy used
// LogSource.Setup (= "SETUP") for exactly this scope.
[EventSource(Name = "Deckle-Setup")]
public sealed class DeckleSetupSource : DeckleEventSource
{
    public static readonly DeckleSetupSource Log = new();

    private DeckleSetupSource() { }

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtSetupInfo       = 1;
    public const int EvtSetupWarning    = 2;
    public const int EvtSetupError      = 3;

    // ── The wizard is a technical prose area: the strict-typed doctrine
    //    applies by level, not per event. Messages remain typed in the payload
    //    sense (one event = one role), but content is free-form. This is the
    //    same exception as DecklePlaygroundSource.

    [Event(EvtSetupInfo,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SetupInfo(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSetupInfo, message);
    }

    [Event(EvtSetupWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SetupWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSetupWarning, message);
    }

    [Event(EvtSetupError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SetupError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSetupError, message);
    }
}
