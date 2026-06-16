using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Management gestures: the destructive operations the base catalog withholds.
// One responsibility — taking objects out of the space — served only to a
// supervised consumer (the host mounts this behind a launch flag). For now the
// single capability is delete; the batch variant is deferred.
//
// delete moves an object to Anytype's RESTORABLE bin, not a hard delete
// (verified live, see JOURNAL). The danger is therefore not irreversibility but
// hitting the wrong target, since the object is named. So delete is a two-step,
// stateless gesture pinned by id:
//
//   • first call (confirm:false) — looks the target up and returns its identity
//     (name, type, id, snippet) WITHOUT deleting anything;
//   • second call (confirm:true) — commits the move to the bin.
//
// The id returned by the preview IS the confirmation handle: confirming with
// that id pins the commit to exactly what was shown, with no server-side token
// to track. A name that resolves ambiguously throws AmbiguousNameException
// (candidate ids listed) before any preview, so the confirm step always targets
// one object.
public sealed class ManagementGestures(AnytypeApiClient api, NameResolver resolver)
{
    public async Task<string> DeleteAsync(
        string selector, bool confirm = false, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        string id = await resolver.ResolveAsync(selector, typeKeys: null, ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);
        string name = DisplayName(obj);
        string type = TypeKey(obj);

        if (!confirm)
        {
            DeckleAnytypeSource.Log.GestureCompleted("delete_preview", Elapsed(started));
            return Preview(id, name, type, obj);
        }

        // The lookup above doubles as existence + identity check; the write lock
        // guards only the destructive call itself (delete is not a read-modify-write).
        using var _ = await api.AcquireWriteScopeAsync("delete", id, ct);
        await api.DeleteObjectAsync(id, ct);

        DeckleAnytypeSource.Log.GestureCompleted("delete", Elapsed(started));
        return $"Mis en corbeille : {name} ({type}). Restaurable depuis la corbeille Anytype.";
    }

    // The preview the first call returns: enough identity to confirm the target is
    // the right one, and the exact recall to commit. The id is spelled out so the
    // model copies it verbatim into the confirming call.
    static string Preview(string id, string name, string type, JsonObject obj)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Mettre en corbeille (réversible) : ").Append(name).Append(" (").Append(type).Append(").\n");
        sb.Append("id : ").Append(id);

        string snippet = FirstLine(obj["snippet"]?.GetValue<string>());
        if (snippet.Length > 0) sb.Append('\n').Append(snippet);

        sb.Append("\nPour confirmer, rappelle delete avec target=\"").Append(id).Append("\" et confirm=true.");
        return sb.ToString();
    }

    static string DisplayName(JsonObject obj)
    {
        string? name = obj["name"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(name)) return name;
        string snippet = FirstLine(obj["snippet"]?.GetValue<string>());
        return snippet.Length > 0 ? snippet : "(sans titre)";
    }

    static string TypeKey(JsonObject obj) => obj["type"]?["key"]?.GetValue<string>() ?? "?";

    static string FirstLine(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int nl = s.IndexOf('\n');
        return (nl < 0 ? s : s[..nl]).Trim();
    }

    static double Elapsed(DateTime startUtc) => (DateTime.UtcNow - startUtc).TotalMilliseconds;
}
