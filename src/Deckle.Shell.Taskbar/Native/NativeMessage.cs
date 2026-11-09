using System.Text.Json;

namespace Deckle.Shell.Taskbar;

// A line the injected DLL sends to Deckle. The wire format is one JSON object
// per line, in UTF-8, with a "t" member that names the kind:
//
//   {"t":"hello","protocol":"1","pid":"1234","module":"C:\\...\\Deckle.Shell.Taskbar.Native.dll"}
//   {"t":"log","level":"info","event":"connected","msg":"...","fields":{"k":"v"}}
//   {"t":"status","generation":"2","settled":"true","rules":[{"id":"...","state":"applied",...}],"behaviors":[...],"problems":["..."]}
//   {"t":"tree","tid":"1234","text":"Type#Name  [props]\n  ..."}    one root of the live visual tree, answering a dump command
//   {"t":"tree_end","roots":"3"}                                     the last record of a dump
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
                "status" => new NativeStatus(
                    Generation: Number(root, "generation"),
                    Settled: Text(root, "settled") == "true",
                    Rules: States(root, "rules"),
                    Behaviors: States(root, "behaviors"),
                    Problems: Strings(root, "problems")),
                "tree" => new NativeTreeDump(Tid: Number(root, "tid"), Text: Text(root, "text")),
                "tree_end" => new NativeTreeEnd(Roots: Number(root, "roots")),
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

    private static IReadOnlyList<NativeRuleState> States(JsonElement parent, string name)
    {
        var states = new List<NativeRuleState>();
        if (parent.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in array.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                states.Add(new NativeRuleState(
                    Id: Text(entry, "id"),
                    State: Text(entry, "state"),
                    Matched: Number(entry, "matched"),
                    Active: Number(entry, "active"),
                    Reasserted: Number(entry, "reasserted"),
                    Contested: Number(entry, "contested"),
                    Error: Text(entry, "error")));
            }
        }
        return states;
    }

    private static IReadOnlyList<string> Strings(JsonElement parent, string name)
    {
        var strings = new List<string>();
        if (parent.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in array.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String) strings.Add(entry.GetString() ?? "");
            }
        }
        return strings;
    }

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

// How one rule, or one behavior, stands in explorer. State is one of:
//   applied        at least one element is held at the rule's value
//   pending        nothing matched yet, within the first 10 seconds
//   never_matched  nothing matched 10 seconds after the document (the element may show up later)
//   contested      Explorer's code keeps undoing the value faster than the engine answers
//   idle           the rule matched, but its elements are gone or its visual state is off
//   error          the rule cannot apply; Error says why
internal sealed record NativeRuleState(
    string Id, string State, int Matched, int Active, int Reasserted, int Contested, string Error);

// The engine's report after a document, and again whenever a number changes.
internal sealed record NativeStatus(
    int Generation,
    bool Settled,
    IReadOnlyList<NativeRuleState> Rules,
    IReadOnlyList<NativeRuleState> Behaviors,
    IReadOnlyList<string> Problems) : NativeMessage;

// One root of the live visual tree, indented text, one element per line.
internal sealed record NativeTreeDump(int Tid, string Text) : NativeMessage;

internal sealed record NativeTreeEnd(int Roots) : NativeMessage;

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
