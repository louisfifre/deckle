using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Folders.App;

// The Deckle Folders app's own story: its start, its end, the exception that
// ends it and the ones it survives. What the window does is Deckle Folders' provider's
// (Deckle-Folders), and a window's creation and load are Deckle-Windowing's.
// A second copy turned away has no sink (the first holds the log open): it
// leaves a note file instead, as the published Deckle's does.
[EventSource(Name = "Deckle-FoldersApp")]
public sealed class DeckleFoldersAppSource : DeckleEventSource
{
    public static readonly DeckleFoldersAppSource Log = new();

    private DeckleFoldersAppSource() { }

    // IDs are public in the ETW manifest; never reuse an id after deleting an
    // event.
    public const int EvtAppStarting = 1;
    public const int EvtAppEnded = 2;
    public const int EvtUnhandledException = 3;
    public const int EvtSurvivedException = 4;
    public const int EvtSurvivedExceptionRepeated = 5;
    public const int EvtSurvivedStack = 6;

    public const int EvtDataRootMoved = 7;
    public const int EvtDataRootNotMoved = 8;
    public const int EvtDataRootMoveFailed = 9;
    public const int EvtModuleFolderKept = 10;

    // The one-time move from the code name's folder (FoldersDataMigration).
    // Folders are named from the suite root, never as full paths.
    [Event(EvtDataRootMoved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Folders moved its data to its new folder")]
    public void DataRootMoved(string from_folder, string to_folder)
    {
        if (IsEnabled()) WriteEvent(EvtDataRootMoved, from_folder, to_folder);
    }

    // The app starts on an empty data root: the folders a person added are
    // in the old folder until one of the two is dealt with.
    [Event(EvtDataRootNotMoved,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Folders found its data in two folders and moved nothing; keep the one you want in apps\\folders and remove the other\nold_folder={0} | new_folder={1}")]
    public void DataRootNotMoved(string old_folder, string new_folder)
    {
        if (IsEnabled()) WriteEvent(EvtDataRootNotMoved, old_folder, new_folder);
    }

    [Event(EvtDataRootMoveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Folders could not move its data to its new folder and starts empty; close any program using apps\\atelier and start it again\nold_folder={0} | new_folder={1} | ex_type={2} | ex_message={3}")]
    public void DataRootMoveFailed(string old_folder, string new_folder, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtDataRootMoveFailed, old_folder, new_folder, ex_type, ex_message);
    }

    [Event(EvtModuleFolderKept,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Folders moved its data but left the old module folder beside the new one; merge modules\\atelier into modules\\folders by hand\nold_folder={0} | new_folder={1}")]
    public void ModuleFolderKept(string old_folder, string new_folder)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderKept, old_folder, new_folder);
    }

    // Which Deckle Folders this is, in the payload: the message stays a sentence.
    [Event(EvtAppStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Folders starting")]
    public void AppStarting(string version, string build, string session, int pid)
    {
        if (IsEnabled()) WriteEvent(EvtAppStarting, version, build, session, pid);
    }

    // Paired with AppStarting by the session and the pid; an AppStarting
    // without it reads as a run that was killed.
    [Event(EvtAppEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Folders ended")]
    public void AppEnded(string session, int pid)
    {
        if (IsEnabled()) WriteEvent(EvtAppEnded, session, pid);
    }

    // The exception ends the app (the AppDomain net).
    [Event(EvtUnhandledException,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Folders stopped on an exception nobody handled\norigin={0} | ex_type={1} | ex_message={2}")]
    public void UnhandledException(string origin, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtUnhandledException, origin, ex_type, ex_message);
    }

    // An exception the UI framework's net caught and marked handled: the app
    // goes on, degraded, so it is a Warning, and the first occurrence per
    // exception type and site opens the incident with its cause on one line
    // (SurvivedExceptionLedger). The stack goes in SurvivedStack beside it.
    [Event(EvtSurvivedException,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An exception was caught and Deckle Folders keeps running\norigin={0} | ex_type={1} | site={2} | ex_message={3}")]
    public void SurvivedException(string origin, string ex_type, string site, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSurvivedException, origin, ex_type, site, ex_message);
    }

    // The same incident again: the log already has the opening line, and a
    // fault that fires on every frame must not fill it.
    [Event(EvtSurvivedExceptionRepeated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "origin={0} | ex_type={1} | site={2} | occurrences={3}")]
    public void SurvivedExceptionRepeated(string origin, string ex_type, string site, int occurrences)
    {
        if (IsEnabled()) WriteEvent(EvtSurvivedExceptionRepeated, origin, ex_type, site, occurrences);
    }

    [Event(EvtSurvivedStack,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "stack={0}")]
    public void SurvivedStack(string stack_trace)
    {
        if (IsEnabled()) WriteEvent(EvtSurvivedStack, stack_trace);
    }
}
