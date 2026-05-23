using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

// Setup wizard provider. Couvre les pages du wizard first-run sous
// src/Deckle/Shell/Setup/ : ChoicesPage (sélection des items à
// télécharger), InstallingPage (orchestration des téléchargements +
// vérifications), SummaryPage (récap final), SetupWindow (cycle de
// vie de la fenêtre).
//
// Provider Name = "Deckle.Setup" → tag [SETUP] via le bridge. Le legacy
// utilisait LogSource.Setup (= "SETUP") pour exactement ce périmètre.
[EventSource(Name = "Deckle.Setup")]
public sealed class DeckleSetupSource : DeckleEventSource
{
    public static readonly DeckleSetupSource Log = new();

    private DeckleSetupSource() { }

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtSetupInfo       = 1;
    public const int EvtSetupWarning    = 2;
    public const int EvtSetupError      = 3;

    // ── Le wizard est une zone de prose technique : la doctrine
    //    strict-typed ne s'applique pas par event mais par niveau.
    //    Les messages restent typés au sens du payload (un event = un
    //    rôle), mais le contenu est libre. C'est la même entorse que
    //    DecklePlaygroundSource.

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
