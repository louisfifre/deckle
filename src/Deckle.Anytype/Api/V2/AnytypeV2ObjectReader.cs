using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Reads an object as one flat AnyBlock document plus its ETag. Every v2 read
// shape — full, outline, one block, one half — comes through here, so a gesture
// that edits what it read hands the ETag back to the writer and the edit fails
// instead of overwriting a concurrent change.
public sealed class AnytypeV2ObjectReader(AnytypeV2Client client)
{
    private readonly AnytypeV2Client _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<AnytypeV2ObjectDocument> ReadAsync(
        string objectId,
        AnytypeV2ObjectReadOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);

        AnytypeV2Response response = await _client.SendAsync(
                new AnytypeV2Request(
                    HttpMethod.Get,
                    $"objects/{objectId}",
                    Query: options?.ToQuery()),
                ct)
            .ConfigureAwait(false);

        if (response.Body is not JsonObject body)
            throw new HttpRequestException("Anytype API returned a non-object JSON root for an object read.");

        // The header is where the ETag was measured on every read shape. The
        // upstream docs also mention an envelope field; it is only a fallback.
        string? etag = response.ETag ?? body["etag"]?.GetValue<string>();
        return new AnytypeV2ObjectDocument(body, etag);
    }
}
