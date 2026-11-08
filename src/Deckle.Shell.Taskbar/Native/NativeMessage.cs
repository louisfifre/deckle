using System.Text.Json;

namespace Deckle.Shell.Taskbar;

// A line the injected DLL sends to Deckle. The wire format is one JSON object
// per line, in UTF-8, with a "t" member that names the kind:
//
//   {"t":"hello","protocol":"1","pid":"1234","module":"C:\\...\\Deckle.Shell.Taskbar.Native.dll"}
//   {"t":"log","level":"info","event":"connected","msg":"...","fields":{"k":"v"}}
//
// Every value is a string, numbers and handles included: an InstanceHandle is
// a 64-bit pointer, which a JSON number (a double in most readers) would round.
internal abstract record NativeMessage
{
    // A line that is not a JSON object, or has no "t", comes back as
    // NativeUnreadableMessage rather than throwing: the pipe is read on a
    // loop that must not die on one bad line.
    public static NativeMessage Parse(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new NativeUnreadableMessage(line);

            return Text(root, "t") switch
            {
                "hello" => new NativeHello(
                    Protocol: Number(root, "protocol"),
                    ProcessId: Number(root, "pid"),
                    ModulePath: Text(root, "module")),
                "log" => new NativeLogRecord(
                    Level: NativeLogLevelNames.Parse(Text(root, "level")),
                    Event: Text(root, "event"),
                    Message: Text(root, "msg"),
                    Fields: Fields(root)),
                _ => new NativeUnreadableMessage(line),
            };
        }
        catch (JsonException)
        {
            return new NativeUnreadableMessage(line);
        }
    }

    private static string Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int Number(JsonElement parent, string name) =>
        int.TryParse(Text(parent, name), out int number) ? number : 0;

    private static IReadOnlyDictionary<string, string> Fields(JsonElement parent)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parent.TryGetProperty("fields", out JsonElement object_) && object_.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in object_.EnumerateObject())
                fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.GetRawText();
        }
        return fields;
    }
}

// The DLL's first line after it connects.
internal sealed record NativeHello(int Protocol, int ProcessId, string ModulePath) : NativeMessage;

// A line the DLL could not make sense of for the host, kept whole for the log.
internal sealed record NativeUnreadableMessage(string Line) : NativeMessage;

internal enum NativeLogLevel
{
    Verbose,
    Info,
    Warning,
    Error,
}

internal static class NativeLogLevelNames
{
    // An unknown name reads as Info: a record from a newer DLL is still shown.
    public static NativeLogLevel Parse(string name) => name switch
    {
        "verbose" => NativeLogLevel.Verbose,
        "warning" => NativeLogLevel.Warning,
        "error" => NativeLogLevel.Error,
        _ => NativeLogLevel.Info,
    };
}

// One log record from inside explorer. `Event` is a stable snake_case name,
// `Message` a sentence for a person, `Fields` the values as text.
internal sealed record NativeLogRecord(
    NativeLogLevel Level,
    string Event,
    string Message,
    IReadOnlyDictionary<string, string> Fields) : NativeMessage
{
    // The fields as "k=v | k=v", the shape the doctrine gives a detail.
    public string FieldsText() =>
        string.Join(" | ", Fields.Select(pair => $"{pair.Key}={pair.Value}"));
}
