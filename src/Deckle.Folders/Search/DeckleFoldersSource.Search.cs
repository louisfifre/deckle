using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Folders;

// The search button's events take the block 80 to 89, given to this work so
// the lanes built beside it each keep their own: two [Event] methods sharing
// an id silence the whole provider
// (tests/Deckle.Folders.Tests/FoldersEventIdBugNotes.md).
//
// They say what the machine offered and which page opened, nothing the
// person typed or keeps.
public sealed partial class DeckleFoldersSource
{
    public const int EvtSearchRouted              = 80;
    public const int EvtCommandPaletteProbeFailed = 81;
    public const int EvtPowerToysPageOpened       = 82;
    public const int EvtPowerToysPageNotOpened    = 83;

    // route: "command-palette" (installed: the panel gives its shortcut) or
    // "install-powertoys" (absent: the panel offers PowerToys' page).
    [Event(EvtSearchRouted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "route={0}")]
    public void SearchRouted(string route)
    {
        if (IsEnabled()) WriteEvent(EvtSearchRouted, route);
    }

    // Windows' package list could not be read: the button offers PowerToys,
    // as when Command Palette is absent.
    [Event(EvtCommandPaletteProbeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Whether Command Palette is installed could not be read; search offers to install PowerToys\nex_type={0}")]
    public void CommandPaletteProbeFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtCommandPaletteProbeFailed, ex_type);
    }

    // page: "store" (PowerToys in the Microsoft Store) or "web" (Microsoft's
    // install page, when no app takes the Store's link).
    [Event(EvtPowerToysPageOpened,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page={0}")]
    public void PowerToysPageOpened(string page)
    {
        if (IsEnabled()) WriteEvent(EvtPowerToysPageOpened, page);
    }

    [Event(EvtPowerToysPageNotOpened,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "PowerToys' page could not be opened; nothing else happens\npage={0} | ex_type={1}")]
    public void PowerToysPageNotOpened(string page, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPowerToysPageNotOpened, page, ex_type);
    }
}
