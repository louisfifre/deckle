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
// showing. So delete is a two-step, stateless gesture pinned by id:
//
//   • first call (confirm:false) — looks the target up and returns its identity
//     (name, type, id, snippet) and how many objects link to it, WITHOUT
//     deleting anything;
//   • second call (confirm:true) — asks the person through IDeletionApproval
//     (a toast the agent does not answer in the chat), then commits.
//
// Any object may be binned, whatever its type (a rapport included) and whatever
// links to it: the safety is the confirmation, so the confirmation must say what
// is being binned. Objects that link to the target (a task a rapport cites, a
// chantier holding its tasks) are not a refusal but a fact stated in the preview
// and on the toast: the bin empties those links. Only a count is told, because
// the backlinks read gives ids and nothing more, and an extra read per linking
// object is not worth the names.
//
// What stays a refusal is not knowing: when the links cannot be read, the
// confirmation could not say what the bin empties, so nothing is binned. The
// links are read again after the person's answer; if their count moved while the
// toast waited, the person approved something other than what is now true, so the
// call refuses and a new preview is asked for.
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

        string id = await resolver.ResolveAsync(selector, typeKeys: null, NameMatch.Exact, ct);
        JsonObject obj = await api.GetObjectAsync(id, ct);
        string name = DisplayName(obj);
        string type = TypeKey(obj);

        int links = await ReadLinksAsync(id, name, type, ct);

        if (!confirm)
        {
            DeckleAnytypeSource.Log.GestureCompleted("delete_preview", Elapsed(started));
            return Preview(id, name, type, obj, links);
        }

        DeletionApprovalOutcome outcome = await AskAsync(new DeletionCandidate(id, name, type, links), ct);
        if (outcome != DeletionApprovalOutcome.Approved)
            throw Refusal(outcome, type, id, name);

        // The person answered on the links the toast showed; what links to the
        // object may have changed while it waited, and then they approved
        // something other than what the bin would empty now.
        int linksNow = await ReadLinksAsync(id, name, type, ct);
        if (linksNow != links)
        {
            DeckleAnytypeSource.Log.DeleteRefused(DeleteRefusalReasons.LinksChanged, type, id, "", "");
            throw new InvalidOperationException(
                $"Refusé : le nombre d'objets qui pointent vers « {name} » a changé pendant l'attente ({links} → {linksNow}) ; "
                + "la personne a validé autre chose que ce que la corbeille viderait maintenant. Rien n'a bougé. "
                + "Refais un aperçu (delete sans confirm), remontre-le à la personne, puis confirme.");
        }

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

    // How many objects link to this one. Links that cannot be read are the one
    // refusal left on this side: the preview and the toast exist to say what the
    // bin empties, and without the links they could not say it, so a bin never
    // goes through on a confirmation that was unclear.
    async Task<int> ReadLinksAsync(string id, string name, string type, CancellationToken ct)
    {
        BacklinkCount backlinks = await CountBacklinksAsync(id, ct);
        if (backlinks.Count is not { } count)
        {
            DeckleAnytypeSource.Log.DeleteRefused(
                DeleteRefusalReasons.ReferencesUnreadable, type, id, backlinks.ExType, backlinks.Cause);
            throw new InvalidOperationException(
                $"Refusé : impossible de lire ce qui pointe vers « {name} » (recherche Anytype en échec, ou objet "
                + "absent de la recherche). Sans cette lecture, la confirmation ne pourrait pas dire ce que la corbeille "
                + "viderait, donc rien ne part à la corbeille. Rien n'a bougé.");
        }

        return count;
    }

    // What links to the object, the object itself left out. Null when it
    // cannot be told: a failed search, or no row for the id (the search never
    // lists a binned object). Unknown is never read as zero: a bin must not go
    // through on a guess about what it empties. An unknown count says why, as an
    // error type and code or as not_listed, so the refusal that follows is not a
    // mystery.
    async Task<BacklinkCount> CountBacklinksAsync(string id, CancellationToken ct)
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
            return row is null
                ? new BacklinkCount(null, "", DeleteRefusalCauses.NotListed)
                : new BacklinkCount(row.Properties.ObjectIds("backlinks").Count(link => link != id), "", "");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BacklinkCount(
                null, ex.GetType().Name,
                ex is AnytypeV2Exception v2
                    ? ApiFailureIncidents.BoundedCode(v2.Code)
                    : DeleteRefusalCauses.SearchFailed);
        }
    }

    // The references of an object, or why they could not be read.
    readonly record struct BacklinkCount(int? Count, string ExType, string Cause);

    static InvalidOperationException Refusal(DeletionApprovalOutcome outcome, string type, string id, string name)
    {
        (string reason, string message) = outcome switch
        {
            DeletionApprovalOutcome.Declined => (DeleteRefusalReasons.Declined,
                $"Refusé : la personne a répondu non à la notification Windows pour « {name} ». Rien n'a bougé."),
            DeletionApprovalOutcome.Unanswered => (DeleteRefusalReasons.Unanswered,
                $"Refusé : pas de réponse à la notification Windows en {ApprovalWindow.TotalSeconds:F0} s (elle a pu "
                + "arriver dans le centre de notifications sans s'afficher). Rien n'a bougé ; redemande à la personne "
                + "avant de réessayer."),
            _ => (DeleteRefusalReasons.ApprovalUnavailable,
                "Refusé : Deckle ne peut pas demander la seconde validation (notifications Windows désactivées, "
                + "Deckle lancé en administrateur, ou hôte sans notification). Rien n'a bougé ; la personne peut "
                + "mettre l'objet à la corbeille elle-même dans Anytype."),
        };
        DeckleAnytypeSource.Log.DeleteRefused(reason, type, id, "", "");
        return new InvalidOperationException(message);
    }

    // archive exists only where the type carries the « Archivé » checkbox. Told
    // only when links exist, the case where hiding without emptying them matters.
    static string ArchiveHint(string type) =>
        DevSpace.PropertiesFor(type).Any(p => p.Key == DevSpace.Props.Archive)
            ? "\nPour seulement ne plus le voir, archive suffit et garde ces liens."
            : "";

    // The id is spelled out so the model copies it verbatim into the confirming
    // call. The links are stated before the instruction to show the target, so
    // that what the person is asked to approve includes what the bin empties.
    static string Preview(string id, string name, string type, JsonObject obj, int links)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Mettre en corbeille (réversible) : ").Append(name).Append(" (").Append(type).Append(").\n");
        sb.Append("id : ").Append(id);

        string snippet = FirstLine(obj["snippet"]?.GetValue<string>());
        if (snippet.Length > 0) sb.Append('\n').Append(snippet);

        if (links > 0)
        {
            sb.Append("\nAttention : ")
              .Append(links == 1
                  ? "1 autre objet pointe vers celui-ci ; la corbeille videra ce lien."
                  : $"{links} autres objets pointent vers celui-ci ; la corbeille videra ces liens.")
              .Append(ArchiveHint(type));
        }

        sb.Append(links > 0
            ? "\nAvant de confirmer, montre cette cible et ces liens à la personne et attends son accord explicite dans la conversation."
            : "\nAvant de confirmer, montre cette cible à la personne et attends son accord explicite dans la conversation.");
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
