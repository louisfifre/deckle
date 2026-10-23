using System.Globalization;

namespace Deckle.Diagnostics;

// One line appended to a small text file, for a process that has no sink to
// write through: a launcher parent that supervises the app, or a second
// instance that is turned away before it may touch the log (the resident
// run holds log.jsonl open for writing, so a rejected one cannot append
// to it).
//
// The file is bounded. Once it passes the cap it starts over, so a fact that
// repeats (every press of a launcher shortcut while Deckle is already
// running) cannot grow it for months. Starting over loses the oldest lines,
// never the newest. A line that cannot be written is dropped: nothing else
// can take it, and the caller goes on.
public static class NoteFile
{
    public const long DefaultMaxBytes = 64 * 1024;

    // Between the timestamp and the text of a line.
    private const string Separator = " | ";

    public static void Append(string path, string line, DateTimeOffset now, long maxBytes = DefaultMaxBytes)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var info = new FileInfo(path);
            if (info.Exists && info.Length > maxBytes) info.Delete();

            File.AppendAllText(
                path,
                $"{now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)}{Separator}{line}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dropped, as described above.
        }
    }

    // One line of a note file, split where Append joined it.
    public readonly record struct Note(string At, string Text);

    // Reads every line the file holds and removes it, so the resident run can
    // carry the facts a sinkless process left into its own log.
    //
    // The file is moved aside before it is read, not read and then deleted: a
    // second instance appending between the read and the delete would lose its
    // line, where after the move its append opens a fresh file for the next
    // take. A take that died after the move leaves the moved copy; the next one
    // returns its lines first. A file that cannot be moved or read stays where
    // it is for the next take, and what was read before the failure is returned.
    public static IReadOnlyList<Note> Take(string path)
    {
        var notes = new List<Note>();
        string taken = path + ".taken";
        try
        {
            if (File.Exists(taken)) ReadAndDelete(taken, notes);
            if (File.Exists(path))
            {
                File.Move(path, taken);
                ReadAndDelete(taken, notes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next take, as described above.
        }
        return notes;
    }

    private static void ReadAndDelete(string path, List<Note> notes)
    {
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0) continue;
            int cut = line.IndexOf(Separator, StringComparison.Ordinal);
            notes.Add(cut < 0
                ? new Note(string.Empty, line)
                : new Note(line[..cut], line[(cut + Separator.Length)..]));
        }
        File.Delete(path);
    }
}