using System.Globalization;

namespace Deckle.Diagnostics;

// Tells a run that ended on its own terms from one that did not, across the
// process boundary the journal cannot see over.
//
// A run that dies by a kill, a power loss or a crash writes no end line, and
// nothing in the journal says why a run has a start and no end. The marker is
// a one-line file written when a run begins and removed when it ends in an
// orderly way. A run that starts and finds the file still there knows the
// previous one never reached its end, and names it.
//
// The file carries the session id of the run that wrote it. End removes the
// file only when the session is its own: a restart spawns the successor before
// the old process has finished exiting, and the successor's marker must not be
// deleted by the process it replaces.
//
// A marker that cannot be read or written never stops the run: the answer is
// "unknown" (no previous run reported) and the run goes on.
public static class RunMarker
{
    public const string FileName = "run.marker";

    private const char Separator = '|';

    // What a run left behind when it did not end. StartedAt is null when the
    // marker was unreadable: the run is known to have been unclean, not which.
    public sealed record PreviousRun(string Session, int Pid, DateTimeOffset? StartedAt);

    // Marks this run as in progress and returns the previous run's leftover
    // marker, if there was one.
    public static PreviousRun? Begin(string directory, string session, int pid, DateTimeOffset now)
    {
        string path = Path.Combine(directory, FileName);
        PreviousRun? previous = Read(path);
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                path,
                string.Join(
                    Separator,
                    session,
                    pid.ToString(CultureInfo.InvariantCulture),
                    now.ToString("o", CultureInfo.InvariantCulture)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a marker the next run reports nothing; this one runs on.
        }
        return previous;
    }

    // Marks this run as ended in an orderly way. Idempotent, and a no-op when
    // the file belongs to another session.
    public static void End(string directory, string session)
    {
        string path = Path.Combine(directory, FileName);
        try
        {
            if (Read(path) is { } current
                && string.Equals(current.Session, session, StringComparison.Ordinal))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next run reports this one as unclean: a false alarm the log
            // can live with, better than a run that fails to exit.
        }
    }

    private static PreviousRun? Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PreviousRun("unreadable", 0, null);
        }

        string[] parts = text.Trim().Split(Separator);
        if (parts.Length == 3
            && parts[0].Length > 0
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            && DateTimeOffset.TryParse(
                parts[2],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset startedAt))
        {
            return new PreviousRun(parts[0], pid, startedAt);
        }

        return new PreviousRun("unreadable", 0, null);
    }
}
