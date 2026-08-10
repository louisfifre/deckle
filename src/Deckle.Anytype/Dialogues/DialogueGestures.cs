using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Dialogue chats are conversation transcripts, not project-management reports.
// They live on Anytype's chat surface; task linkage is optional metadata on the
// chat object, while turns themselves go through the Chat API.
public sealed class DialogueGestures(AnytypeApiClient api, NameResolver resolver)
{
    readonly AnytypeApiClient _api = api;
    readonly NameResolver _resolver = resolver;

    public async Task<string> CreateAsync(
        string title,
        string mode,
        string brief,
        string? task = null,
        CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        title = Required(title, nameof(title));
        mode = Mode(Required(mode, nameof(mode)));
        brief = Required(brief, nameof(brief));

        JsonObject chat = await _api.CreateChatAsync(new JsonObject { ["name"] = title }, ct);
        string chatId = Id(chat, "Le chat créé n'a pas d'id.");

        string? taskId = null;
        if (!string.IsNullOrWhiteSpace(task))
        {
            taskId = await _resolver.ResolveAsync(task, [DevSpace.Types.Task], ct);
            await _api.UpdateObjectAsync(
                chatId,
                new JsonObject
                {
                    ["properties"] = new JsonArray(ObjectsProp(DevSpace.Props.TachesLiees, [taskId])),
                },
                ct);
        }

        string opening = $"Mode : {mode}\n\nBrief :\n{brief.Trim()}";
        JsonObject message = await _api.AddChatMessageAsync(
            chatId,
            MessagePayload("system", opening),
            ct);

        DeckleAnytypeSource.Log.GestureCompleted("dialogue_create", Elapsed(t0));
        return $"Chat créé : {DisplayName(chat)}\nchat_id : {chatId}\nmessage_id : {MessageId(message)}"
             + (taskId is null ? "" : $"\ntâche liée : {taskId}");
    }

    public async Task<string> PostAsync(
        string chat,
        string speaker,
        string text,
        CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        // dialogue_create returns this handle. A non-idempotent append must not
        // recover through a display name that can be ambiguous or renamed.
        string chatId = AnytypeObjectId.Require(chat, "chat");
        JsonObject message = await _api.AddChatMessageAsync(
            chatId,
            MessagePayload(speaker, Required(text, nameof(text))),
            ct);

        DeckleAnytypeSource.Log.GestureCompleted("dialogue_post", Elapsed(t0));
        return $"Message ajouté : {MessageId(message)}";
    }

    public async Task<string> ReadAsync(
        string chat,
        string? afterOrderId = null,
        CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();

        string chatId = await ResolveChatAsync(chat, ct);
        JsonObject root = await _api.GetChatMessagesAsync(chatId, afterOrderId, ct: ct);
        JsonArray messages = root["messages"] as JsonArray ?? [];

        var sb = new StringBuilder();
        sb.Append("chat_id : ").Append(chatId);

        string? lastOrderId = null;
        foreach (JsonNode? node in messages)
        {
            if (node is not JsonObject message) continue;
            lastOrderId = message["order_id"]?.GetValue<string>() ?? lastOrderId;
        }

        if (lastOrderId is not null)
            sb.Append('\n').Append("dernier_order_id : ").Append(lastOrderId);

        foreach (JsonNode? node in messages)
            if (node is JsonObject message)
                sb.Append('\n').Append(Render(message));

        if (messages.Count == 0)
            sb.Append('\n').Append("Aucun message.");

        DeckleAnytypeSource.Log.GestureCompleted("dialogue_read", Elapsed(t0));
        return sb.ToString();
    }

    async Task<string> ResolveChatAsync(string selector, CancellationToken ct) =>
        await _resolver.ResolveAsync(Required(selector, nameof(selector)), [DevSpace.Types.Chat], ct);

    static JsonObject MessagePayload(string speaker, string text) => new()
    {
        ["text"] = $"[{Speaker(speaker)}]\n{text.Trim()}",
        ["style"] = "paragraph",
    };

    static JsonObject ObjectsProp(string key, IReadOnlyList<string> ids)
    {
        var arr = new JsonArray();
        foreach (string id in ids) arr.Add(JsonValue.Create(id));
        return new JsonObject { ["key"] = key, ["objects"] = arr };
    }

    static string Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Argument « {name} » vide.", name);
        return value.Trim();
    }

    static string Mode(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "start" => "start",
        "challenge" => "challenge",
        "dialogue" => "dialogue",
        _ => throw new ArgumentException(
            "Mode inconnu. Modes attendus : start, challenge, dialogue.",
            nameof(mode)),
    };

    static string Speaker(string speaker) => speaker.Trim().ToLowerInvariant() switch
    {
        "system" => "System",
        "claude" => "Claude",
        "codex" => "Codex",
        "louis" => "Louis",
        _ => throw new ArgumentException(
            "Intervenant inconnu. Intervenants attendus : system, claude, codex, louis.",
            nameof(speaker)),
    };

    static string Id(JsonObject obj, string error) =>
        obj["id"]?.GetValue<string>() ?? throw new InvalidOperationException(error);

    static string MessageId(JsonObject response) =>
        response["message_id"]?.GetValue<string>() ?? "(id inconnu)";

    static string DisplayName(JsonObject obj) =>
        obj["name"]?.GetValue<string>() is { Length: > 0 } name ? name : "(sans titre)";

    static string Render(JsonObject message)
    {
        string text = message["content"]?["text"]?.GetValue<string>() ?? "";
        text = text.TrimEnd();
        if (text.Length == 0) text = "(message vide)";
        if (LooksPrefixed(text)) return text;

        string creator = message["creator_name"]?.GetValue<string>() ?? "?";
        return $"[{creator}]\n{text}";
    }

    static bool LooksPrefixed(string text)
    {
        if (!text.StartsWith("[", StringComparison.Ordinal)) return false;
        int close = text.IndexOf(']');
        return close > 1 && close < 20;
    }

    static double Elapsed(long t0) =>
        Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
}
