using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

public static class DialogueToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> Build(DialogueGestures dialogues)
    {
        return new ToolDescriptor[]
        {
            new(
                "dialogue_create",
                "Create an Anytype chat for a mediated LLM discussion. Use for codex-start, codex-challenge, and codex-dialogue. The brief is posted as the first System message; task is optional and links the chat to a task when relevant.",
                Schema(
                    required:
                    [
                        Prop("title", "string", "Chat title."),
                        Prop("mode", "string", "Dialogue mode.", oneOf: ["start", "challenge", "dialogue"]),
                        Prop("brief", "string", "Validated brief to seed the discussion."),
                    ],
                    optional:
                    [
                        Prop("task", "string", "Optional task name or id to link to the chat."),
                    ]),
                async (args, ct) =>
                    await dialogues.CreateAsync(
                        Str(args, "title"),
                        Str(args, "mode"),
                        Str(args, "brief"),
                        StrOpt(args, "task"),
                        ct)),

            new(
                "dialogue_post",
                "Post one turn to a dialogue chat. Speaker is written into the message text because the current API actor may be Louis or a future bot.",
                Schema(
                    required:
                    [
                        Prop("chat", "string", "Dialogue chat name or id."),
                        Prop("speaker", "string", "Speaker label.", oneOf: ["system", "claude", "codex", "louis"]),
                        Prop("text", "string", "Message text."),
                    ]),
                async (args, ct) =>
                    await dialogues.PostAsync(
                        Str(args, "chat"),
                        Str(args, "speaker"),
                        Str(args, "text"),
                        ct)),

            new(
                "dialogue_read",
                "Read dialogue chat messages. Pass after_order_id to continue from the last order id previously returned.",
                Schema(
                    required:
                    [
                        Prop("chat", "string", "Dialogue chat name or id."),
                    ],
                    optional:
                    [
                        Prop("after_order_id", "string", "Return messages after this order id."),
                    ]),
                async (args, ct) =>
                    await dialogues.ReadAsync(
                        Str(args, "chat"),
                        StrOpt(args, "after_order_id"),
                        ct)),
        };
    }

    static JsonObject Schema(IReadOnlyList<(string Name, JsonObject Schema)>? required = null,
                             IReadOnlyList<(string Name, JsonObject Schema)>? optional = null)
    {
        var properties = new JsonObject();
        var requiredNames = new JsonArray();

        if (required is not null)
            foreach (var (name, schema) in required)
            {
                properties[name] = schema;
                requiredNames.Add(name);
            }

        if (optional is not null)
            foreach (var (name, schema) in optional)
                properties[name] = schema;

        var root = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        if (requiredNames.Count > 0) root["required"] = requiredNames;
        return root;
    }

    static (string, JsonObject) Prop(string name, string type, string description,
                                     IReadOnlyList<string>? oneOf = null)
    {
        var schema = new JsonObject { ["type"] = type, ["description"] = description };
        if (oneOf is not null) schema["enum"] = ToEnum(oneOf);
        return (name, schema);
    }

    static JsonArray ToEnum(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values) array.Add(value);
        return array;
    }

    static string Str(JsonObject? args, string name)
    {
        var value = StrOpt(args, name);
        if (value is null) throw new ArgumentException($"Missing required argument '{name}'.", name);
        return value;
    }

    static string? StrOpt(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var s))
            throw new ArgumentException($"Argument '{name}' must be a string.", name);
        return s;
    }
}
