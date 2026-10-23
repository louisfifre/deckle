namespace Deckle.Diagnostics;

// Opens a JSONL file for appending, and first makes sure the next row starts
// on a line of its own.
//
// A process killed, or a disk that fills, while a row is being written leaves
// the file ending in the middle of an object. Appending to it as it stands
// would glue the next row to the torn one, and a reader would lose both: the
// torn row is unrecoverable, the new one should not be lost with it. One
// newline closes the torn line, so the damage stays on that line.
internal static class JsonlAppendStream
{
    public static FileStream Open(string path)
    {
        bool endsMidRow = EndsMidRow(path);

        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (endsMidRow)
        {
            stream.WriteByte((byte)'\n');
            stream.Flush();
        }

        return stream;
    }

    private static bool EndsMidRow(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (probe.Length == 0) return false;

            probe.Seek(-1, SeekOrigin.End);
            return probe.ReadByte() != '\n';
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }
}
