using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Deckle.Diagnostics;

// Shared JSONL encoding for fixed and routed sinks. The writer thread reuses
// one ArrayBufferWriter, avoiding the former MemoryStream + ToArray allocation
// for every observation while still serializing a complete line before I/O.
internal sealed class JsonlLineSerializer
{
    private readonly ArrayBufferWriter<byte> _buffer = new(512);

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ReadOnlyMemory<byte> Serialize(
        EventEntry entry,
        string kindLabel,
        JsonlSchema schema)
    {
        _buffer.Clear();
        using var writer = new Utf8JsonWriter(_buffer, JsonOptions);

        writer.WriteStartObject();
        writer.WriteString("timestamp", entry.Timestamp.ToString("o", CultureInfo.InvariantCulture));
        writer.WriteString("kind", kindLabel);
        writer.WriteString("session", DeckleEventSource.SessionId);
        if (schema == JsonlSchema.SelfDescribing)
        {
            writer.WriteString("provider", entry.Provider);
            writer.WriteString("event", entry.EventName);
            writer.WriteString("level", entry.Level.ToString());
            writer.WriteString("source", LogLineFormatter.MapSource(entry.Provider));
            if (entry.FormattedMessage is null) writer.WriteNull("message");
            else writer.WriteString("message", entry.FormattedMessage);
            writer.WriteString("line", LogLineFormatter.Format(entry));
        }
        writer.WritePropertyName("payload");
        writer.WriteStartObject();
        foreach ((string name, object? value) in entry.Payload)
            WriteValue(writer, name, value);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();

        return _buffer.WrittenMemory;
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:               writer.WriteNull(name); break;
            case string s:           writer.WriteString(name, s); break;
            case bool b:             writer.WriteBoolean(name, b); break;
            case int i:              writer.WriteNumber(name, i); break;
            case long l:             writer.WriteNumber(name, l); break;
            case short sh:           writer.WriteNumber(name, sh); break;
            case byte by:            writer.WriteNumber(name, by); break;
            case uint ui:            writer.WriteNumber(name, ui); break;
            case ulong ul:           writer.WriteNumber(name, ul); break;
            case ushort us:          writer.WriteNumber(name, us); break;
            case sbyte sb:           writer.WriteNumber(name, sb); break;
            case float f:            writer.WriteNumber(name, f); break;
            case double d:           writer.WriteNumber(name, d); break;
            case Guid g:             writer.WriteString(name, g.ToString()); break;
            case DateTime dt:        writer.WriteString(name, dt.ToString("o", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset dto: writer.WriteString(name, dto.ToString("o", CultureInfo.InvariantCulture)); break;
            default:                 writer.WriteString(name, value.ToString() ?? string.Empty); break;
        }
    }
}
