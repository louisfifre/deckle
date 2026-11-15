using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Atelier;

// The file tool's events. EventIds 50-59 belong to it, so the tools built side
// by side each add theirs without taking one another's numbers.
//
// An item's name and path are the person's own disk: an event carries the
// action, the extension, a reason and an error code, never a name or a path.
public sealed partial class DeckleAtelierSource
{
    public const int EvtItemLaunched    = 50;
    public const int EvtItemNotLaunched = 51;

    // action: "open" (the file's default application) or "show-in-folder"
    // (File Explorer, the item selected).
    [Event(EvtItemLaunched,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "action={0} | extension={1}")]
    public void ItemLaunched(string action, string extension)
    {
        if (IsEnabled()) WriteEvent(EvtItemLaunched, action, extension);
    }

    // The item was gone, or the shell refused it: the list goes on, and the
    // person is told in the window.
    [Event(EvtItemNotLaunched,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An item could not be handed to Windows; the list goes on\naction={0} | extension={1} | reason={2} | error_code={3}")]
    public void ItemNotLaunched(string action, string extension, string reason, int error_code)
    {
        if (IsEnabled()) WriteEvent(EvtItemNotLaunched, action, extension, reason, error_code);
    }
}
