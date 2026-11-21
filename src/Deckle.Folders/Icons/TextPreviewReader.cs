using System.Text;

namespace Deckle.Folders;

// The beginning of a Markdown or plain-text file, as the gallery shows it on
// the file's tile (spec, Behaviours: "it also shows PDFs and Markdown files
// large"). Plain text, never rendered: a heading's "#" stays a "#".
//
// Bounded whatever the file weighs: at most MaxBytes are read, and at most
// MaxLines lines kept, so a log of a gigabyte costs what a note of ten lines
// costs. A tile shows about eight lines of the caption style; the rest is
// margin for wrapped lines.
//
// Encodings, the ones a text file on Windows comes in: a byte order mark says
// UTF-8, UTF-16 little or big endian; without one the bytes are taken as
// UTF-8 when they are valid UTF-8, as Windows-1252 otherwise, the code page
// Notepad wrote French text in before UTF-8. Bytes that hold a NUL are not
// text, whatever their extension says, and show no preview.
//
// Never throws for a file it cannot read (gone, locked, refused): it answers
// null, and the tile keeps its type icon. A placeholder whose content is in
// the cloud is not read, since reading it would download it.
public static class TextPreviewReader
{
    public const int MaxBytes = 2048;
    public const int MaxLines = 12;

    // The types the gallery shows as text. Interpretation: the two the spec
    // and the task name, Markdown and plain text; code and data files keep
    // their icon.
    private static readonly HashSet<string> Extensions =
        new([".md", ".markdown", ".txt"], StringComparer.OrdinalIgnoreCase);

    public static bool Previews(string path) => Extensions.Contains(Path.GetExtension(path));

    public static string? Read(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || !FileContentPresence.IsLocal(file.Attributes)) return null;

            byte[] buffer = new byte[MaxBytes];
            int read;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.SequentialScan))
            {
                read = stream.ReadAtLeast(buffer, MaxBytes, throwOnEndOfStream: false);
            }
            return Decode(buffer.AsSpan(0, read), truncated: read == MaxBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // The preview of these bytes, the first ones of a file. truncated says
    // the file goes on past them, so a character cut at the end is dropped
    // rather than shown as a replacement mark. Null when there is nothing to
    // show: no text, or not text.
    public static string? Decode(ReadOnlySpan<byte> bytes, bool truncated)
    {
        string text = DecodeCharacters(bytes, truncated);
        if (text.Contains('\0')) return null;

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        // A last line cut by the byte limit is still shown: the tile cuts it
        // anyway.
        IEnumerable<string> kept = lines
            .Select(l => l.Replace('\t', ' ').TrimEnd())
            .SkipWhile(l => l.Length == 0)
            .Take(MaxLines);
        string preview = string.Join('\n', kept).TrimEnd();
        return preview.Length == 0 ? null : preview;
    }

    private static string DecodeCharacters(ReadOnlySpan<byte> bytes, bool truncated)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            return Utf8(bytes[3..], truncated) ?? Windows1252.GetString(bytes[3..]);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            return Utf16(Encoding.Unicode, bytes[2..]);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return Utf16(Encoding.BigEndianUnicode, bytes[2..]);
        return Utf8(bytes, truncated) ?? Windows1252.GetString(bytes);
    }

    // Null when the bytes are not valid UTF-8. A sequence the byte limit cut
    // is dropped first: at most three bytes, the most a character's beginning
    // takes before its last byte.
    private static string? Utf8(ReadOnlySpan<byte> bytes, bool truncated)
    {
        if (truncated) bytes = bytes[..CompleteUtf8Length(bytes)];
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static int CompleteUtf8Length(ReadOnlySpan<byte> bytes)
    {
        for (int back = 1; back <= Math.Min(3, bytes.Length); back++)
        {
            byte b = bytes[^back];
            if ((b & 0xC0) == 0x80) continue; // a continuation byte: look further back
            int needed = (b & 0x80) == 0 ? 1 : (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : (b & 0xF8) == 0xF0 ? 4 : 1;
            return needed > back ? bytes.Length - back : bytes.Length;
        }
        return bytes.Length;
    }

    // An odd last byte is half a character the limit cut, and a lone high
    // surrogate at the end half of a pair: both are dropped.
    private static string Utf16(Encoding encoding, ReadOnlySpan<byte> bytes)
    {
        int length = bytes.Length & ~1;
        if (length >= 2)
        {
            bool bigEndian = encoding.CodePage == Encoding.BigEndianUnicode.CodePage;
            char last = (char)(bigEndian
                ? bytes[length - 2] << 8 | bytes[length - 1]
                : bytes[length - 1] << 8 | bytes[length - 2]);
            if (char.IsHighSurrogate(last)) length -= 2;
        }
        return encoding.GetString(bytes[..length]);
    }

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Windows-1252 ships with .NET in CodePagesEncodingProvider, which is not
    // registered by default; Latin-1, its near neighbour, if it ever is not
    // there.
    private static readonly Encoding Windows1252 =
        CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;
}
