using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// A successful /v2 answer: the parsed body plus the parts that travel in
// headers. ETag keeps its quotes so it can be handed back to If-Match as is.
// Replayed is true when the server answered an Idempotency-Key it had already
// seen, with the stored result of the first call. RateLimitRemaining is the
// budget left in the shared limiter; only writes carry it, so it is null on
// reads.
public sealed record AnytypeV2Response(
    int Status,
    JsonNode? Body,
    string? ETag,
    bool Replayed,
    int? RateLimitRemaining);
