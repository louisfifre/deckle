using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Projects;

// Deckle Projects' provider. What it writes describes the machine: counts,
// durations, reasons and error types. A project's folder, its name, a
// category's title and an action's id are the person's own, so no event
// carries one of them (an action id is a script name).
//
// The events live in one partial file per concern, each with its own block of
// ids, so work done side by side never takes the same number (two [Event]
// methods sharing an id silence the whole provider, see
// tests/Deckle.Folders.Tests/FoldersEventIdBugNotes.md):
//
//    1-9    the project store                 (Store/)
//   10-19   what is read from a folder        (Readouts/)
//   20-29   the script menu                   (ScriptMenu/)
//   30-39   the actions that run              (Runs/)
//   40-49   terminal launching and links      (Opening/)
//   50-59   the window and its pages          (Presentation/)
//
// IDs are public in the ETW manifest: never reuse one after deleting an event,
// a consumer may still need it to decode a dump.
[EventSource(Name = "Deckle-Projects")]
public sealed partial class DeckleProjectsSource : DeckleEventSource
{
    public static readonly DeckleProjectsSource Log = new();

    private DeckleProjectsSource() { }
}
