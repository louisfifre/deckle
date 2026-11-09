using System.Text;

namespace Deckle.Shell.Taskbar;

// The framing of the pipe: one JSON document per line, UTF-8, "\n" at the end.
// A JSON document written compactly never contains a raw line break (the
// writers escape it inside strings), so a line is a message. A StreamReader splits the incoming side, decoding
// multi-byte characters across reads.
//
// Byte mode with lines, not message mode: a message-mode read that the buffer
// is too small for returns ERROR_MORE_DATA and the reader must stitch the
// pieces, which is the same work as splitting lines, with more ways to go wrong
// on the C++ side.
internal static class JsonLineFraming
{
    public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // The bytes of one outgoing message. Refuses text that holds a line break,
    // which would be read as two messages.
    public static byte[] Frame(string json)
    {
        if (json.Contains('\n') || json.Contains('\r'))
            throw new ArgumentException("A message is one line; compact the JSON first.", nameof(json));
        return Utf8.GetBytes(json + "\n");
    }
}

