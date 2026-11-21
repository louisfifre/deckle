using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Folders;

// The gallery's previews of a file's content. EventIds 66-69 belong to them,
// inside the file tool's block (60-69, Launching/DeckleFoldersSource.Launching.cs),
// so the lanes built side by side each add theirs without taking one
// another's numbers. Two events sharing a number put the whole source in
// error and it then writes nothing at all.
//
// Like the rest of the provider, an event here carries the kind of preview,
// a reason and an error code, never a name or a path.
public sealed partial class DeckleFoldersSource
{
    public const int EvtPreviewNotDrawn = 66;

    // kind: "pdf". reason: "protected" (the document asks for a password) or
    // "unreadable" (not a PDF Windows can read, gone, locked or refused).
    // The tile keeps its type icon and the gallery goes on; the provider
    // remembers the answer, so a file is told once while it stays the same.
    [Event(EvtPreviewNotDrawn,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A file's preview could not be drawn; its tile keeps its type icon\nkind={0} | reason={1} | ex_type={2} | hresult={3}")]
    public void PreviewNotDrawn(string kind, string reason, string ex_type, int hresult)
    {
        if (IsEnabled()) WriteEvent(EvtPreviewNotDrawn, kind, reason, ex_type, hresult);
    }
}
