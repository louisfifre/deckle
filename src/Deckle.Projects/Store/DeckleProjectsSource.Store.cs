using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Projects;

// The project store's block, ids 1 to 9. An event here carries counts and
// error types, never a project's path or name, a category's title or an
// action's id.
public sealed partial class DeckleProjectsSource
{
    public const int EvtStoreLoadFailed   = 1;
    public const int EvtStoreSaveFailed   = 2;
    public const int EvtProjectAdded      = 3;
    public const int EvtProjectAlreadyKnown = 4;
    public const int EvtProjectRemoved    = 5;
    public const int EvtCategoryCreated   = 6;
    public const int EvtCategoryDeleted   = 7;
    public const int EvtProjectMoved      = 8;

    // The file exists and cannot be read: the window opens with no project,
    // and the unreadable file is kept aside so nothing is lost.
    [Event(EvtStoreLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The projects could not be read; Deckle Projects starts with none\nex_type={0}")]
    public void StoreLoadFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtStoreLoadFailed, ex_type);
    }

    [Event(EvtStoreSaveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The projects could not be saved; the change holds until the window closes\nex_type={0}")]
    public void StoreSaveFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtStoreSaveFailed, ex_type);
    }

    [Event(EvtProjectAdded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A project was added to Deckle Projects")]
    public void ProjectAdded(long projects)
    {
        if (IsEnabled()) WriteEvent(EvtProjectAdded, projects);
    }

    // Adding a folder that is already a project opens its page and adds
    // nothing (spec, Behaviours).
    [Event(EvtProjectAlreadyKnown,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A folder that is already a project was added again; nothing changed")]
    public void ProjectAlreadyKnown()
    {
        if (IsEnabled()) WriteEvent(EvtProjectAlreadyKnown);
    }

    // Only the entry goes: nothing on the disk is touched.
    [Event(EvtProjectRemoved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A project was removed from Deckle Projects; nothing on the disk changed")]
    public void ProjectRemoved(long projects)
    {
        if (IsEnabled()) WriteEvent(EvtProjectRemoved, projects);
    }

    [Event(EvtCategoryCreated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A category was created")]
    public void CategoryCreated(long categories)
    {
        if (IsEnabled()) WriteEvent(EvtCategoryCreated, categories);
    }

    // The projects of a deleted category go back to none; none is removed.
    [Event(EvtCategoryDeleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A category was deleted; its projects were kept")]
    public void CategoryDeleted(long projects_released)
    {
        if (IsEnabled()) WriteEvent(EvtCategoryDeleted, projects_released);
    }

    [Event(EvtProjectMoved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A project was moved to another category")]
    public void ProjectMoved()
    {
        if (IsEnabled()) WriteEvent(EvtProjectMoved);
    }
}
