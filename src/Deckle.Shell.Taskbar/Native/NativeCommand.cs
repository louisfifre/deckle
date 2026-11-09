using System.Text;
using System.Text.Json;

namespace Deckle.Shell.Taskbar;

// A line Deckle sends to the injected DLL: one JSON object per line, UTF-8,
// with a "t" member that names the command.
//
//   {"t":"stop"}              restore everything, unadvise, unload the DLL
//   {"t":"ping"}              the DLL answers with a "pong" log record
//   {"t":"apply","doc":{..}}  the settings document: rules and behaviors (see TaskbarDocumentBuilder)
//   {"t":"dump"}              the DLL answers with "tree" records, then "tree_end"
internal static class NativeCommand
{
    public const string Stop = """{"t":"stop"}""";
    public const string Ping = """{"t":"ping"}""";
    public const string Dump = """{"t":"dump"}""";

    // Wraps a settings document. The document is re-written compactly, so
    // whatever whitespace the caller's text holds cannot break the one line.
    public static string Apply(string documentJson)
    {
        using JsonDocument document = JsonDocument.Parse(documentJson);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("t", "apply");
            writer.WritePropertyName("doc");
            document.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
