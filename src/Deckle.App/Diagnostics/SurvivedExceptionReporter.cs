using System;
using Deckle.Diagnostics;

namespace Deckle.App;

// What the app writes when one of its nets catches an exception and the process
// goes on: the UI framework's net marks it handled, the task scheduler's marks it
// observed. Work continued degraded, so the line is a Warning; the exceptions that
// do end the process keep their Error (see the AppDomain net in App).
//
// A fault that comes back on every frame or tick is one incident: the ledger
// decides which occurrence opens it, this writes the opening line with the stack
// beside it, and the repeats only count at Verbose.
internal static class SurvivedExceptionReporter
{
    private static readonly SurvivedExceptionLedger Ledger = new();

    // True when this occurrence opened the incident.
    public static bool Report(string origin, Exception exception)
    {
        SurvivedException seen = Ledger.Note(origin, exception);

        if (!seen.OpensIncident)
        {
            DeckleAppSource.Log.SurvivedExceptionRepeated(
                seen.Origin, seen.Type, seen.Site, seen.Occurrences);
            return false;
        }

        // An exception message can quote a path under the user's profile.
        DeckleAppSource.Log.SurvivedException(
            seen.Origin, seen.Type, seen.Site, PathRedaction.Redact(seen.Message));
        // The whole text, inner exceptions and their stacks included: the line above
        // names the cause, this one is where to look.
        DeckleAppSource.Log.CrashStackTrace(PathRedaction.Redact(exception.ToString()));
        return true;
    }
}
