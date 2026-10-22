using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Turns a list of ops into the request body {"ops":[…]} for one endpoint. The
// server takes 1 to 512 entries and applies them in order as one edit; checking
// the count and the endpoint here means a mistake costs no request, and the
// shared limiter is 1 rps with dry runs spending it too.
internal static class AnytypeV2OpBatch
{
    private const int MaxOps = 512;

    internal static JsonObject ToBody(IReadOnlyList<AnytypeV2Op> ops, AnytypeV2OpScope endpoint)
    {
        ArgumentNullException.ThrowIfNull(ops);
        if (ops.Count is < 1 or > MaxOps)
            throw new ArgumentException($"A batch holds 1 to {MaxOps} ops, not {ops.Count}.", nameof(ops));

        var array = new JsonArray();
        for (int index = 0; index < ops.Count; index++)
        {
            AnytypeV2Op op = ops[index];
            if (!op.Scope.HasFlag(endpoint))
                throw new ArgumentException(
                    $"ops[{index}] is {op.Kind}, which a {endpoint.ToString().ToLowerInvariant()} PATCH does not accept.",
                    nameof(ops));
            array.Add(op.Json.DeepClone());
        }
        return new JsonObject { ["ops"] = array };
    }
}
