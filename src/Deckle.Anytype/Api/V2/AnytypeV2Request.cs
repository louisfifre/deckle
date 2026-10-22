using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// One /v2 call, described independently of how it is sent. Path is relative to
// the space (objects/{id}, types/{key}); the client owns the /v2/spaces/{id}
// prefix. IfMatch is an ETag exactly as a read returned it, quotes included.
internal sealed record AnytypeV2Request(
    HttpMethod Method,
    string Path,
    JsonNode? Body = null,
    IReadOnlyList<KeyValuePair<string, string>>? Query = null,
    string? IfMatch = null,
    string? IdempotencyKey = null)
{
    // A GET never changes anything. A write is safe to send twice only when the
    // server can recognize the second copy, which is what the key is for.
    public bool IsReplaySafe => Method == HttpMethod.Get || IdempotencyKey is not null;
}
