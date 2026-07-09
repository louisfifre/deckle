using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

public sealed partial class AnytypeApiClient
{
    public async Task<JsonObject> ListTypesAsync(
        string spaceId,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        string path = $"{SpacePath(spaceId)}/types?offset={offset}&limit={limit}";
        return await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
    }

    public async Task<JsonObject> CreateTypeAsync(
        string spaceId,
        JsonObject payload,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentNullException.ThrowIfNull(payload);

        JsonObject root = await SendAsync(
            HttpMethod.Post, $"{SpacePath(spaceId)}/types", payload, ct).ConfigureAwait(false);
        return InnerOrRoot(root, "type");
    }

    public async Task<JsonObject> UpdateTypeAsync(
        string spaceId,
        string typeId,
        JsonObject payload,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        ArgumentNullException.ThrowIfNull(payload);

        JsonObject root = await SendAsync(
            HttpMethod.Patch, $"{SpacePath(spaceId)}/types/{typeId}", payload, ct).ConfigureAwait(false);
        return InnerOrRoot(root, "type");
    }

    public async Task<JsonObject> ListPropertiesForSpaceAsync(
        string spaceId,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        string path = $"{SpacePath(spaceId)}/properties?offset={offset}&limit={limit}";
        return await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
    }

    public async Task<JsonObject> CreatePropertyAsync(
        string spaceId,
        JsonObject payload,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentNullException.ThrowIfNull(payload);

        JsonObject root = await SendAsync(
            HttpMethod.Post, $"{SpacePath(spaceId)}/properties", payload, ct).ConfigureAwait(false);
        return InnerOrRoot(root, "property");
    }

    public async Task<JsonObject> ListPropertyTagsForSpaceAsync(
        string spaceId,
        string propertyId,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);

        string path = $"{SpacePath(spaceId)}/properties/{propertyId}/tags?offset={offset}&limit={limit}";
        return await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
    }

    public async Task<JsonObject> CreatePropertyTagAsync(
        string spaceId,
        string propertyId,
        JsonObject payload,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        ArgumentNullException.ThrowIfNull(payload);

        JsonObject root = await SendAsync(
            HttpMethod.Post, $"{SpacePath(spaceId)}/properties/{propertyId}/tags", payload, ct)
            .ConfigureAwait(false);
        return InnerOrRoot(root, "tag");
    }

    private static string SpacePath(string spaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        return $"/v1/spaces/{spaceId}";
    }

    private static JsonObject InnerOrRoot(JsonObject root, string key) =>
        root[key] as JsonObject
        ?? root["object"] as JsonObject
        ?? root;
}
