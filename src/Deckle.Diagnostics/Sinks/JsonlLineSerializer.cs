using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Deckle.Diagnostics;

// Shared JSONL encoding for fixed and routed sinks. The writer thread reuses
// one ArrayBufferWriter, so the returned memory is valid only until the next
// Serialize call; a complete line, newline included, is serialized before any
// I/O.
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
            writer.WriteString("keywords", TransverseKeywordNames(entry));
            writer.WriteString("source", LogLineFormatter.MapSource(entry.Provider));
            // Only when set: app.jsonl mixes the milestones the journal keeps
            // with plain detail, and a reader needs to tell them apart. Rows
            // without the mark read exactly as they did before.
            if (entry.Journal) writer.WriteBoolean("journal", true);
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
        // The terminator belongs to the line: a sink writes it in one call, so
        // a row can never be left without its newline by a failure in between.
        _buffer.Write("\n"u8);

        return _buffer.WrittenMemory;
    }

    // Names of the transverse flags (bits 0..9) in enum declaration order, e.g.
    // "Lifecycle, Capture". Provider-local bits (10..63) mean nothing outside
    // their provider, so the mask drops them rather than printing raw numbers.
    private static string TransverseKeywordNames(EventEntry entry)
    {
        const long TransverseMask = 0x3FF;
        var transverse = (Keywords)((long)entry.Keywords & TransverseMask);
        // [Flags] ToString() renders a zero value as "None"; the field is empty instead.
        return transverse == Keywords.None ? string.Empty : transverse.ToString();
    }

    // JSON has no literal for NaN or the infinities, and Utf8JsonWriter throws
    // on them. A throw here would cost the whole row and, through the sink's
    // fault path, close the stream and open a sink incident, so a measurement
    // that went off the scale (a division by zero in a ratio, say) travels as
    // its invariant text instead: "NaN", "Infinity", "-Infinity".
    private static void WriteFloatingPoint(Utf8JsonWriter writer, string name, double value)
    {
        if (double.IsFinite(value)) writer.WriteNumber(name, value);
        else writer.WriteString(name, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteFloatingPoint(Utf8JsonWriter writer, string name, float value)
    {
        if (float.IsFinite(value)) writer.WriteNumber(name, value);
        else writer.WriteString(name, value.ToString(CultureInfo.InvariantCulture));
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
            case float f:            WriteFloatingPoint(writer, name, f); break;
            case double d:           WriteFloatingPoint(writer, name, d); break;
            case Guid g:             writer.WriteString(name, g.ToString()); break;
            case DateTime dt:        writer.WriteString(name, dt.ToString("o", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset dto: writer.WriteString(name, dto.ToString("o", CultureInfo.InvariantCulture)); break;
            default:                 writer.WriteString(name, value.ToString() ?? string.Empty); break;
        }
    }
}
