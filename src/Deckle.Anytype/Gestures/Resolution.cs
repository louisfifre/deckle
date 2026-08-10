using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Shared name→id resolution consumed by every gesture class. A gesture argument
// that designates an object accepts either its name or its id; this is the one
// place that turns a name into an id by searching the space.

// One search hit, trimmed to what an ambiguity message needs to show the model.
public sealed record Candidate(string Id, string Name, string TypeKey, string? Snippet);

// A name matched several objects. The message lists the candidates (id + name +
// type) so the model can retry with an explicit id — the designed UX, not a
// failure to hide.
public sealed class AmbiguousNameException : Exception
{
    public string Query { get; }
    public IReadOnlyList<Candidate> Candidates { get; }

    public AmbiguousNameException(string query, IReadOnlyList<Candidate> candidates)
        : base(BuildMessage(query, candidates))
    {
        Query = query;
        Candidates = candidates;
    }

    static string BuildMessage(string query, IReadOnlyList<Candidate> candidates)
    {
        var lines = candidates.Select(c =>
            $"  {c.Id}  {c.Name} ({c.TypeKey})");
        return $"« {query} » correspond à plusieurs objets — relance avec un id :\n"
             + string.Join("\n", lines);
    }
}

public sealed class NotFoundException : Exception
{
    public string Query { get; }

    public NotFoundException(string query)
        : base($"Aucun objet trouvé pour « {query} ».")
    {
        Query = query;
    }
}

// Stable provider identity used by replayable mutations. A display name is a
// useful read selector, but it is not a recovery coordinate: the mutation may
// rename it before its response reaches the caller.
public static class AnytypeObjectId
{
    // Anytype object ids are CIDv1 (base32): they start with "bafy" and are
    // substantially longer than schema keys or display names.
    public static bool Is(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string candidate = value.Trim();
        return candidate.Length > 40
            && candidate.StartsWith("bafy", StringComparison.Ordinal);
    }

    public static string Require(string? value, string parameterName)
    {
        if (!Is(value))
            throw new ArgumentException(
                $"L’argument « {parameterName} » doit être un id Anytype stable (bafy…).",
                parameterName);
        return value!.Trim();
    }
}

public sealed class NameResolver(AnytypeApiClient api)
{
    readonly AnytypeApiClient _api = api;

    // Resolves a selector (name or id) to an object id, restricting the search
    // to typeKeys when given (null → any type). Exact case-insensitive name
    // match wins; a single fuzzy hit is taken; several throw AmbiguousName; none
    // throws NotFound.
    public async Task<string> ResolveAsync(
        string selector, IReadOnlyList<string>? typeKeys, CancellationToken ct = default)
        => await ResolveAsync(_api.SpaceId, selector, typeKeys, ct).ConfigureAwait(false);

    // Cross-space utilities resolve inside an allow-listed space alias while the
    // legacy project-management gestures keep using the credentials' default
    // space through the overload above.
    public async Task<string> ResolveAsync(
        string spaceId,
        string selector,
        IReadOnlyList<string>? typeKeys,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        if (string.IsNullOrWhiteSpace(selector))
            throw new NotFoundException(selector ?? "");

        selector = selector.Trim();
        if (AnytypeObjectId.Is(selector)) return selector;

        var candidates = await SearchCandidatesAsync(spaceId, selector, typeKeys, ct);
        if (candidates.Count == 0) throw new NotFoundException(selector);

        var exact = candidates
            .Where(c => string.Equals(c.Name, selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count == 1) return exact[0].Id;
        if (exact.Count > 1) throw new AmbiguousNameException(selector, exact);

        if (candidates.Count == 1) return candidates[0].Id;
        throw new AmbiguousNameException(selector, candidates);
    }

    async Task<List<Candidate>> SearchCandidatesAsync(
        string spaceId,
        string query,
        IReadOnlyList<string>? typeKeys,
        CancellationToken ct)
    {
        var root = await _api.SearchAsync(spaceId, query, typeKeys, limit: 20, ct);
        var data = root["data"] as JsonArray;
        var result = new List<Candidate>();
        if (data is null) return result;

        foreach (var node in data)
        {
            if (node is not JsonObject obj) continue;
            string id = obj["id"]?.GetValue<string>() ?? "";
            if (id.Length == 0) continue;
            result.Add(new Candidate(
                id,
                DisplayName(obj),
                obj["type"]?["key"]?.GetValue<string>() ?? "",
                obj["snippet"]?.GetValue<string>()));
        }
        return result;
    }

    // Note-layout objects (rapport) carry an empty name; their title is the
    // first line of the snippet. Mirrors the vendor helper's getDisplayName.
    static string DisplayName(JsonObject obj)
    {
        string? name = obj["name"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(name)) return name;
        string? snippet = obj["snippet"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(snippet))
        {
            int nl = snippet.IndexOf('\n');
            return nl >= 0 ? snippet[..nl] : snippet;
        }
        return "(sans titre)";
    }
}
