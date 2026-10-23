using System.Text;

namespace Deckle.TestSupport;

// ── SharedFileReader ──────────────────────────────────────────────────────────
//
// Reads a file that another handle in the same process still holds open for
// writing, the way a test reads a JSONL file while its sink is alive.
//
// File.ReadAllText / ReadAllLines / ReadLines open the file with
// FileShare.Read, which asks Windows to refuse any handle that has write
// access. A live JsonlSink holds exactly such a handle, so the read fails with
// "being used by another process" even though the sink is only waiting. The
// reader here opens with FileShare.ReadWrite, which accepts the writer's
// handle, and reads what the writer has flushed so far.
//
// The production sink keeps FileShare.Read on purpose, so nothing else writes
// the file behind its back; only the reader adapts.
public static class SharedFileReader
{
    public static string ReadAllText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static string[] ReadAllLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines.ToArray();
    }
}
