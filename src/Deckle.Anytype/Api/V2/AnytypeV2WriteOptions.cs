namespace Deckle.Anytype;

// What a write can carry beyond its body.
//
//   • IfMatch — the ETag of the version this edit was computed from (the
//     AnytypeV2ObjectDocument.ETag of the read, quotes included). If the object
//     changed since, the write is refused with a 409 etag_mismatch instead of
//     overwriting; re-read, re-plan, retry. Null writes unconditionally. PATCH
//     only: a create has no earlier version.
//   • IdempotencyKey — names this intent. The server answers a repeat of the
//     same key and body with the stored result (Replayed = true) instead of
//     acting twice; the same key with another body is a 409
//     idempotency_conflict. Null makes the writer generate a fresh key per call
//     so the transport can resend after a 5xx without duplicating. Pass your
//     own to make a re-run of a whole gesture safe too. The server keeps keys in
//     memory only: a backend restart forgets them.
//   • DryRun — validate and report what would change (diff_stats, created_blocks,
//     the template a create would use) without writing. It still spends the
//     shared rate-limit budget.
//   • FullIds — answer with full block ids instead of the compact suffixes.
//   • CreateMissingOptions — let a write create select options it names
//     (type ops: add_property options). Property values on an object create
//     unknown option names on their own.
public sealed record AnytypeV2WriteOptions
{
    public string? IfMatch { get; init; }

    public string? IdempotencyKey { get; init; }

    public bool DryRun { get; init; }

    public bool FullIds { get; init; }

    public bool CreateMissingOptions { get; init; }

    internal IReadOnlyList<KeyValuePair<string, string>> ToQuery()
    {
        var query = new List<KeyValuePair<string, string>>();
        if (DryRun) query.Add(new("dry_run", "true"));
        if (FullIds) query.Add(new("ids", "full"));
        if (CreateMissingOptions) query.Add(new("create_missing_options", "true"));
        return query;
    }
}
