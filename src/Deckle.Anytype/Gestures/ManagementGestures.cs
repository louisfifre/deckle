using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Management gestures: the destructive operations the base catalog withholds.
// One responsibility — taking objects out of the space — served only to a
// supervised consumer (the host mounts this only on the client surface that
// opts in).
//
// delete moves an object to Anytype's RESTORABLE bin, not a hard delete. The
// danger is therefore not irreversibility but a wrong bin going unnoticed: the
// v2 search leaves binned objects out, so no later read shows it again. The
// bin is for what should never have existed (a duplicate, a botched creation);
// « Archivé » (archive) is the gesture for what had a life and should only stop
// showing. So delete is a two-step, stateless gesture pinned by id, fenced on
// both sides:
//
//   • first call (confirm:false) — looks the target up and returns its identity
//     (name, type, id, snippet) WITHOUT deleting anything;
//   • second call (confirm:true) — asks the person through IDeletionApproval
//     (a toast the agent does not answer in the chat), then commits.
//
// Both calls refuse, whatever anyone says, a rapport (a session's trace is
// never binned) and an object another object links to (a chantier still
// holding its tasks, a task a rapport cites): the bin would empty those links
// without a word. The links are read again after the person's answer, so a
// link added while the toast waited still stops the delete.
//
// The id returned by the preview IS the confirmation handle: confirming with
// that id pins the commit to exactly what was shown, with no server-side token
// to track. A name that resolves ambiguously throws AmbiguousNameException
// (candidate ids listed) before any preview, so the confirm step always targets
// one object.
public sealed class ManagementGestures(
    AnytypeApiClient api, NameResolver resolver, IDeletionApproval? approval = null)
{
    // Long enough to read a toast and click, short enough that the MCP call and
    // the host's shutdown drain are not held for minutes by a toast nobody saw.
    public static readonly TimeSpan ApprovalWindow = TimeSpan.FromSeconds(60);

    private readonly IDeletionApproval _approval = approval ?? DeletionApproval.Current;
    private readonly AnytypeV2Searcher _searcher = new(api.V2);

    public async Task<string> DeleteAsync(
        string selector, bool confirm = false, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;

        if (confirm)
            selector = AnytypeObjectId.Require(selector, "target");

        string id = await resolver.ResolveAsync(selector, typeKeys: null, ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);
        string name = DisplayName(obj);
        string type = TypeKey(obj);

        await RefuseIfFencedAsync(id, name, type, ct);

        if (!confirm)
        {
            DeckleAnytypeSource.Log.GestureCompleted("delete_preview", Elapsed(started));
            return Preview(id, name, type, obj);
        }

        DeletionApprovalOutcome outcome = await AskAsync(new DeletionCandidate(id, name, type), ct);
        if (outcome != DeletionApprovalOutcome.Approved)
            throw Refusal(outcome, type, id, name);

        // The person answered on what was true before the toast; what links to
        // the object may have changed while it waited.
        await RefuseIfFencedAsync(id, name, type, ct);

        // The lookup above doubles as existence + identity check; the write lock
        // guards only the destructive call itself (delete is not a read-modify-write).
        using var _ = await api.AcquireWriteScopeAsync("delete", id, ct);
        await api.DeleteObjectAsync(id, ct);

        DeckleAnytypeSource.Log.ObjectBinned(type, id);
        DeckleAnytypeSource.Log.GestureCompleted("delete", Elapsed(started));
        return $"Mis en corbeille : {name} ({type}). Restaurable depuis la corbeille Anytype.";
    }

    // The window is the gesture's own policy, so an approval that never answers
    // reads as Unanswered here rather than as a cancelled call.
    async Task<DeletionApprovalOutcome> AskAsync(DeletionCandidate candidate, CancellationToken ct)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(ApprovalWindow);
        try
        {
            return await _approval.RequestAsync(candidate, window.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return DeletionApprovalOutcome.Unanswered;
        }
    }

    async Task RefuseIfFencedAsync(string id, string name, string type, CancellationToken ct)
    {
        if (type == DevSpace.Types.Rapport)
        {
            DeckleAnytypeSource.Log.DeleteRefused("rapport", type, id);
            throw new InvalidOperationException(
                "Refusé : un rapport ne va jamais à la corbeille, il garde la trace d'une session. Rien n'a bougé.");
        }

        int? backlinks = await CountBacklinksAsync(id, ct);
        if (backlinks is null)
        {
            DeckleAnytypeSource.Log.DeleteRefused("references_unreadable", type, id);
            throw new InvalidOperationException(
                $"Refusé : impossible de lire ce qui pointe vers « {name} » (recherche Anytype en échec, ou objet "
                + "absent de la recherche). Sans cette vérification, rien ne part à la corbeille. Rien n'a bougé.");
        }

        if (backlinks > 0)
        {
            DeckleAnytypeSource.Log.DeleteRefused("referenced", type, id);
            throw new InvalidOperationException(
                $"Refusé : {backlinks} autre(s) objet(s) pointent vers « {name} » ({type}) ; la corbeille "
                + "viderait ces liens sans bruit. Rien n'a bougé. " + Alternative(type));
        }
    }

    // What links to the object, the object itself left out. Null when it
    // cannot be told: a failed search, or no row for the id (the search never
    // lists a binned object). Unknown is refused like referenced: the check
    // exists to stop a bin, never to let one through by default.
    async Task<int?> CountBacklinksAsync(string id, CancellationToken ct)
    {
        try
        {
            AnytypeV2SearchPage page = await _searcher.SearchPageAsync(
                new AnytypeV2SearchRequest
                {
                    Filters = [AnytypeV2SearchFilter.Equal("id", id)],
                    Fields = ["backlinks"],
                },
                offset: 0, limit: 1, ct);

            AnytypeV2SearchRow? row = page.Rows.FirstOrDefault(r => r.Id == id);
            return row?.Properties.ObjectIds("backlinks").Count(link => link != id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    static InvalidOperationException Refusal(DeletionApprovalOutcome outcome, string type, string id, string name)
    {
        (string reason, string message) = outcome switch
        {
            DeletionApprovalOutcome.Declined => ("declined",
                $"Refusé : la personne a répondu non à la notification Windows pour « {name} ». Rien n'a bougé."),
            DeletionApprovalOutcome.Unanswered => ("unanswered",
                $"Refusé : pas de réponse à la notification Windows en {ApprovalWindow.TotalSeconds:F0} s (elle a pu "
                + "arriver dans le centre de notifications sans s'afficher). Rien n'a bougé ; redemande à la personne "
                + "avant de réessayer."),
            _ => ("approval_unavailable",
                "Refusé : Deckle ne peut pas demander la seconde validation (notifications Windows désactivées, "
                + "Deckle lancé en administrateur, ou hôte sans notification). Rien n'a bougé ; la personne peut "
                + "mettre l'objet à la corbeille elle-même dans Anytype."),
        };
        DeckleAnytypeSource.Log.DeleteRefused(reason, type, id);
        return new InvalidOperationException(message);
    }

    // archive exists only where the type carries the « Archivé » checkbox.
    static string Alternative(string type) =>
        DevSpace.PropertiesFor(type).Any(p => p.Key == DevSpace.Props.Archive)
            ? "Pour seulement ne plus le voir, utilise archive. S'il doit vraiment disparaître, la personne le met à la corbeille elle-même dans Anytype."
            : "S'il doit vraiment disparaître, la personne le met à la corbeille elle-même dans Anytype.";

    // The id is spelled out so the model copies it verbatim into the confirming
    // call.
    static string Preview(string id, string name, string type, JsonObject obj)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Mettre en corbeille (réversible) : ").Append(name).Append(" (").Append(type).Append(").\n");
        sb.Append("id : ").Append(id);

        string snippet = FirstLine(obj["snippet"]?.GetValue<string>());
        if (snippet.Length > 0) sb.Append('\n').Append(snippet);

        sb.Append("\nAvant de confirmer, montre cette cible à la personne et attends son accord explicite dans la conversation.");
        sb.Append("\nPour confirmer, rappelle delete avec target=\"").Append(id).Append("\" et confirm=true : ");
        sb.Append("Deckle lui redemandera alors par une notification Windows et attendra ")
          .Append(ApprovalWindow.TotalSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))
          .Append(" s.");
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
