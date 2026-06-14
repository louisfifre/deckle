using System.Text.Json.Nodes;

namespace Deckle.Anytype;

public sealed partial class AnytypeApiClient
{
    public async Task<JsonObject> CreateChatAsync(JsonObject payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        JsonObject root = await SendAsync(HttpMethod.Post, $"{_spacePath}/chats", payload, ct)
            .ConfigureAwait(false);
        return Inner(root, "object");
    }

    public async Task<JsonObject> GetChatMessagesAsync(
        string chatId,
        string? afterOrderId = null,
        int limit = 30,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);

        string path = $"{_spacePath}/chats/{chatId}/messages?limit={limit}";
        if (!string.IsNullOrWhiteSpace(afterOrderId))
            path += $"&after_order_id={Uri.EscapeDataString(afterOrderId)}";

        return await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
    }

    public async Task<JsonObject> AddChatMessageAsync(
        string chatId,
        JsonObject payload,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentNullException.ThrowIfNull(payload);

        return await SendAsync(
            HttpMethod.Post,
            $"{_spacePath}/chats/{chatId}/messages",
            payload,
            ct).ConfigureAwait(false);
    }
}
