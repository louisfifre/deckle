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

// A write named an object by a name that no object carries exactly. The nearest
// objects are listed (id + name + type) so the model can retry with the id of
// the one it meant; the write itself is never attempted on a guess.
public sealed class InexactNameException : Exception
{
    public string Query { get; }
    public IReadOnlyList<Candidate> Candidates { get; }

    public InexactNameException(string query, IReadOnlyList<Candidate> candidates)
        : base(BuildMessage(query, candidates))
    {
        Query = query;
        Candidates = candidates;
    }

    static string BuildMessage(string query, IReadOnlyList<Candidate> candidates)
    {
        var lines = candidates.Select(c =>
            $"  {c.Id}  {c.Name} ({c.TypeKey})");
        return $"Aucun objet ne porte exactement le nom « {query} », et une écriture ne devine pas "
             + "un nom — relance avec l'id de l'objet voulu, parmi les plus proches :\n"
             + string.Join("\n", lines);
    }
}

// How strictly a name must match to resolve. A gesture that only reads can take
// the one object a name resembles; a gesture that writes must not, because a
// wrong guess changes the wrong object and nobody sees it happen.
public enum NameMatch
{
    // A lone inexact hit is accepted (reads).
    Tolerant,
    // Only a name equal to the selector (case-insensitive) resolves; anything
    // else is refused with the candidates, so the caller retries with an id (writes).
    Exact,
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

public sealed class NameResolver(AnytypeApiClient api, AnytypeV2Client? v2 = null)
{
    // Same cap the v1 search used: enough candidates to list in an ambiguity message.
    const int CandidateLimit = 20;

    // An exact match reads the search page after page: a page of this many hits,
    // up to this many hits in all (the v1 search page maximum).
    const int ExactPageSize = AnytypeV2Searcher.PageSize;
    const int ExactScanCeiling = AnytypeApiClient.SearchPageMax;

    readonly AnytypeApiClient _api = api;

    // The v2 client is bound to the default space, so only that space rides on
    // v2; an allow-listed foreign space stays on the v1 search.
    readonly AnytypeV2Searcher? _searcher = v2 is null ? null : new AnytypeV2Searcher(v2);
    readonly string? _v2SpaceId = v2?.SpaceId;

    public async Task<string> ResolveAsync(
        string selector, IReadOnlyList<string>? typeKeys, NameMatch match, CancellationToken ct = default)
        => await ResolveAsync(_api.SpaceId, selector, typeKeys, match, ct).ConfigureAwait(false);

    // Cross-space utilities resolve inside an allow-listed space alias; the
    // project-management gestures use the credentials' default space through
    // the overload above.
    public async Task<string> ResolveAsync(
        string spaceId,
        string selector,
        IReadOnlyList<string>? typeKeys,
        NameMatch match,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        if (string.IsNullOrWhiteSpace(selector))
            throw new NotFoundException(selector ?? "");

        selector = selector.Trim();
        if (AnytypeObjectId.Is(selector)) return selector;

        var candidates = await SearchCandidatesAsync(spaceId, selector, typeKeys, match, ct);
        if (candidates.Count == 0) throw new NotFoundException(selector);

        var exact = candidates
            .Where(c => string.Equals(c.Name, selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count == 1) return exact[0].Id;
        if (exact.Count > 1) throw new AmbiguousNameException(selector, exact);

        // No object carries the name. A read may take the one object it
        // resembles; a write refuses and offers the nearest ones by id.
        if (match == NameMatch.Exact)
            throw new InexactNameException(selector, candidates.Take(CandidateLimit).ToList());

        if (candidates.Count == 1) return candidates[0].Id;
        throw new AmbiguousNameException(selector, candidates);
    }

    // A tolerant read looks at one page of CandidateLimit hits: enough to take a
    // lone hit or list the ambiguity. An exact match is a different question:
    // the server ranks the hits, so the object named exactly can sit behind
    // them, and a page of 20 would miss it. It reads page after page up to
    // ExactScanCeiling hits, which a name query does not come near in practice.
    // Past the ceiling an exact name is not seen and the write is refused with
    // the nearest candidates: it fails closed, it never guesses.
    async Task<List<Candidate>> SearchCandidatesAsync(
        string spaceId,
        string query,
        IReadOnlyList<string>? typeKeys,
        NameMatch match,
        CancellationToken ct)
    {
        int pageSize = match == NameMatch.Exact ? ExactPageSize : CandidateLimit;
        int ceiling = match == NameMatch.Exact ? ExactScanCeiling : 0;

        if (_searcher is not null && spaceId == _v2SpaceId)
            return await SearchCandidatesV2Async(query, typeKeys, pageSize, ceiling, ct);

        var result = new List<Candidate>();
        int offset = 0;
        do
        {
            var root = await _api.SearchAsync(spaceId, query, typeKeys, offset, pageSize, ct);
            int returned = 0;
            if (root["data"] is JsonArray data)
                foreach (var node in data)
                {
                    returned++;
                    if (node is not JsonObject obj) continue;
                    string id = obj["id"]?.GetValue<string>() ?? "";
                    if (id.Length == 0) continue;
                    result.Add(new Candidate(
                        id,
                        DisplayName(obj),
                        obj["type"]?["key"]?.GetValue<string>() ?? "",
                        obj["snippet"]?.GetValue<string>()));
                }

            // The next page starts after the rows the server served, and an
            // empty page ends the loop whatever has_more claims.
            bool hasMore = root["pagination"]?["has_more"]?.GetValue<bool>() ?? false;
            if (!hasMore || returned == 0) break;
            offset += returned;
        }
        while (offset < ceiling);
        return result;
    }

    // v2 takes ONE type per search, so several types cost one search each; the
    // hits are merged by id. A v2 row has no snippet (measured 2026-10-22), so
    // an object without a name, a report, reads « (sans titre) » in an
    // ambiguity message, where v1 showed the first line of its snippet.
    // Exact-name matching is unaffected: it only concerns named objects.
    async Task<List<Candidate>> SearchCandidatesV2Async(
        string query, IReadOnlyList<string>? typeKeys, int pageSize, int ceiling, CancellationToken ct)
    {
        IReadOnlyList<string?> types = typeKeys is { Count: > 0 } ? typeKeys : [null];

        var result = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? type in types)
        {
            int offset = 0;
            do
            {
                AnytypeV2SearchPage page = await _searcher!.SearchPageAsync(
                    new AnytypeV2SearchRequest { Type = type, Query = query }, offset, pageSize, ct);
                foreach (AnytypeV2SearchRow row in page.Rows)
                    if (seen.Add(row.Id))
                        result.Add(new Candidate(
                            row.Id,
                            row.Name.Length > 0 ? row.Name : "(sans titre)",
                            row.Type,
                            Snippet: null));

                // Advance by the rows the server sent, never by the page asked
                // for, and stop on an empty page: see AnytypeV2Searcher.SearchAllAsync.
                if (!page.HasMore || page.Returned == 0) break;
                offset += page.Returned;
            }
            while (offset < ceiling);
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
