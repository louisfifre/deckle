using System.Globalization;

namespace Deckle.Diagnostics;

// The pairing identifier of a piece of lasting work (a take, a streaming
// session, a rewrite, a file transcription, a recovery). The deckle-logging
// doctrine asks every start and every end of such work to carry the same
// identifier, so a reader holding only the journal file can match an end to
// its start, or notice that a start never got one.
//
// Why a process-wide counter and not a GUID: the journal row already carries
// the session id (DeckleEventSource.SessionId), so the pair (session, work id)
// is unique in the file. What the identifier must be is short, cheap to emit
// on a hot path, and easy to grep, which a counter in hex is and a 36-character
// GUID is not. One counter shared by every provider means two kinds of work
// never share an identifier either.
//
// Usage. Take the identifier once, when the work begins, and pass the same
// string as the `work_id` payload of the start event, of the end event, and of
// every recovery or incident line that belongs to that work:
//
//     string workId = WorkId.Next();
//     Log.RewriteStarted(workId, ...);
//     ...
//     Log.RewriteEnded(workId, outcome, ...);
//
// `FieldName` is the payload name every provider uses, so a filter on it works
// across modules.
public static class WorkId
{
    // The payload name of the identifier on an event.
    public const string FieldName = "work_id";

    private static long _counter;

    // The next identifier of this process: "w" and a lowercase hexadecimal
    // ordinal, "w1", "w2", …, "w1f". Thread-safe; never repeats during one run.
    public static string Next()
        => "w" + Interlocked.Increment(ref _counter).ToString("x", CultureInfo.InvariantCulture);
}
