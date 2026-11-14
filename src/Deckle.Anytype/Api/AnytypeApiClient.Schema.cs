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
        return await SendAsync(
            HttpMethod.Get, path, null, ct, replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
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
            HttpMethod.Patch, $"{SpacePath(spaceId)}/types/{typeId}", payload, ct,
            replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
        return InnerOrRoot(root, "type");
    }

    // A type's templates. POST /objects never applies a type's default
    // template on its own: the caller resolves one here and sends its id as
    // `template_id` in the create body.
    public async Task<JsonObject> ListTemplatesAsync(
        string spaceId,
        string typeId,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);

        string path = $"{SpacePath(spaceId)}/types/{typeId}/templates?offset={offset}&limit={limit}";
        return await SendAsync(
            HttpMethod.Get, path, null, ct, replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
    }

    public async Task<JsonObject> ListPropertiesForSpaceAsync(
        string spaceId,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        string path = $"{SpacePath(spaceId)}/properties?offset={offset}&limit={limit}";
        return await SendAsync(
            HttpMethod.Get, path, null, ct, replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
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

    // PATCH /properties/{id}: a rename. Measured on a fresh space (2026-11-09):
    // it renames a native property (status, done, due_date) and answers 200.
    public async Task<JsonObject> UpdatePropertyAsync(
        string spaceId,
        string propertyId,
        JsonObject payload,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        ArgumentNullException.ThrowIfNull(payload);

        JsonObject root = await SendAsync(
            HttpMethod.Patch, $"{SpacePath(spaceId)}/properties/{propertyId}", payload, ct,
            replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
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
        return await SendAsync(
            HttpMethod.Get, path, null, ct, replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
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

    // PATCH /tags/{id}: name and colour of an option that exists. v2 writes a colour
    // only when it creates an option; this is the one route that changes it after
    // (color-options.ps1 on Project management, 2026-11-12).
    public async Task<JsonObject> UpdatePropertyTagAsync(
        string spaceId,
        string propertyId,
        string tagId,
        JsonObject payload,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        ArgumentNullException.ThrowIfNull(payload);

        JsonObject root = await SendAsync(
            HttpMethod.Patch, $"{SpacePath(spaceId)}/properties/{propertyId}/tags/{tagId}", payload, ct,
            replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
        return InnerOrRoot(root, "tag");
    }

    // DELETE /tags/{id}: v2 has no route that removes an option.
    public async Task DeletePropertyTagAsync(
        string spaceId,
        string propertyId,
        string tagId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);

        await SendAsync(
            HttpMethod.Delete, $"{SpacePath(spaceId)}/properties/{propertyId}/tags/{tagId}", null, ct,
            parseBody: false).ConfigureAwait(false);
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
