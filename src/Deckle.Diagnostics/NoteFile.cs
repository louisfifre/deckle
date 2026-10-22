using System.Globalization;

namespace Deckle.Diagnostics;

// One line appended to a small text file, for a process that has no sink to
// write through: a launcher parent that supervises the app, or a second
// instance that is turned away before it may touch the journal (the resident
// run holds journal.jsonl open for writing, so a rejected one cannot append
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
                $"{now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)} | {line}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dropped, as described above.
        }
    }
}
