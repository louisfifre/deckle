using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Writes to /v2: an ops batch on an object or on a type, and object creation.
// It owns the three things every write needs and a read does not — the body
// shape, the If-Match / Idempotency-Key headers, and the receipt — so a gesture
// only says what to change.
//
// Every write carries an Idempotency-Key, generated when the caller gave none.
// A non-idempotent request that the transport cannot tell apart from a new one
// can never be resent after a 5xx; with a key it can, and the server answers
// the replay with the first result. A failure throws AnytypeV2Exception.
public sealed class AnytypeV2Writer(AnytypeV2Client client)
{
    private readonly AnytypeV2Client _client = client ?? throw new ArgumentNullException(nameof(client));

    // PATCH /objects/{id}. Atomic: one refused op and nothing is written; the
    // exception's OpIndex names it.
    public Task<AnytypeV2WriteReceipt> ApplyObjectOpsAsync(
        string objectId,
        IReadOnlyList<AnytypeV2Op> ops,
        AnytypeV2WriteOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        return SendAsync(
            HttpMethod.Patch,
            $"objects/{objectId}",
            AnytypeV2OpBatch.ToBody(ops, AnytypeV2OpScope.Object),
            options,
            ct);
    }

    // PATCH /types/{key}. Property ops are written first, then view ops; a view
    // op refused after that leaves the property changes written.
    public Task<AnytypeV2WriteReceipt> ApplyTypeOpsAsync(
        string typeKey,
        IReadOnlyList<AnytypeV2Op> ops,
        AnytypeV2WriteOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        return SendAsync(
            HttpMethod.Patch,
            $"types/{typeKey}",
            AnytypeV2OpBatch.ToBody(ops, AnytypeV2OpScope.Type),
            options,
            ct);
    }

    // POST /objects. Naming no template applies the type's default one and says
    // so in the receipt (Template.Source == type_default); naming one is
    // reported as request. The body is the caller's, because what a create
    // takes depends on the type; IfMatch has no meaning here and is refused.
    public Task<AnytypeV2WriteReceipt> CreateObjectAsync(
        JsonObject body,
        AnytypeV2WriteOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (options?.IfMatch is not null)
            throw new ArgumentException("A create has no earlier version to match.", nameof(options));

        return SendAsync(HttpMethod.Post, "objects", body.DeepClone(), options, ct);
    }

    private async Task<AnytypeV2WriteReceipt> SendAsync(
        HttpMethod method,
        string path,
        JsonNode body,
        AnytypeV2WriteOptions? options,
        CancellationToken ct)
    {
        AnytypeV2Response response = await _client.SendAsync(
                new AnytypeV2Request(
                    method,
                    path,
                    body,
                    options?.ToQuery(),
                    options?.IfMatch,
                    options?.IdempotencyKey ?? Guid.NewGuid().ToString("N")),
                ct)
            .ConfigureAwait(false);

        return new AnytypeV2WriteReceipt(
            response.Status,
            response.Body as JsonObject,
            response.ETag,
            response.Replayed,
            response.RateLimitRemaining);
    }
}
