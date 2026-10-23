namespace Deckle.Home;

// A batch gesture writes through several requests that Anytype cannot make
// atomic: one create or patch per item, then collection membership. When one
// of them fails after another succeeded, the space holds part of the batch.
// Rethrowing the raw failure would hide that, and a caller that repeats the
// whole batch would duplicate what already exists. This exception states what
// was written, what was interrupted and what was never attempted, so the
// caller repeats only the rest. Nothing is rolled back: a delete would be a
// second write that can fail the same way.
public sealed class HomePartialWriteException : Exception
{
    public HomePartialWriteException(
        string message,
        string gesture,
        IReadOnlyList<string> applied,
        string? interrupted,
        IReadOnlyList<string> notApplied,
        Exception inner)
        : base(message, inner)
    {
        Gesture = gesture;
        Applied = applied;
        Interrupted = interrupted;
        NotApplied = notApplied;
    }

    // "create" or "update".
    public string Gesture { get; }

    // Items whose writes all went through, as the gesture's digest names them.
    public IReadOnlyList<string> Applied { get; }

    // The item whose writes stopped half way, or null when the failure fell
    // between items. It may be written in part, or not at all.
    public string? Interrupted { get; }

    // Items the gesture never reached.
    public IReadOnlyList<string> NotApplied { get; }

    internal static HomePartialWriteException ForCreate(
        string type,
        IReadOnlyList<string> created,
        string? interrupted,
        IReadOnlyList<string> notAttempted,
        HomeMembershipState membership,
        int total,
        Exception cause)
    {
        var lines = new List<string>
        {
            $"Création interrompue : {created.Count} objet(s) sur {total} déjà créé(s) dans l’espace ; le lot n’est appliqué qu’en partie.",
            "Créés :",
        };
        lines.AddRange(created.Select(item => $"- {type} · {item}"));
        lines.Add(membership switch
        {
            HomeMembershipState.Applied => "Appartenance aux collections : appliquée aux objets créés.",
            HomeMembershipState.Unconfirmed =>
                "Appartenance aux collections : non confirmée ; pose-la avec update (add_to_collections) sur les objets créés, sans les recréer.",
            _ => "Aucune appartenance à des collections n’était demandée.",
        });
        if (interrupted is not null)
            lines.Add($"Échec sur : {interrupted} — la création n’est pas confirmée ; vérifie avec get avant de la refaire.");
        if (notAttempted.Count > 0)
        {
            lines.Add("Jamais tentés :");
            lines.AddRange(notAttempted.Select(item => "- " + item));
        }
        lines.Add("Ne renvoie pas le lot entier : ne crée que ce qui manque.");
        lines.Add("Cause : " + cause.Message);
        return new HomePartialWriteException(
            string.Join("\n", lines), "create", created, interrupted, notAttempted, cause);
    }

    internal static HomePartialWriteException ForUpdate(
        IReadOnlyList<string> applied,
        string? interrupted,
        string? stage,
        IReadOnlyList<string> notAttempted,
        Exception cause)
    {
        var lines = new List<string>
        {
            "Mise à jour interrompue : le lot n’est appliqué qu’en partie.",
        };
        if (applied.Count > 0)
        {
            lines.Add("Appliqués :");
            lines.AddRange(applied.Select(item => "- " + item));
        }
        if (interrupted is not null)
            lines.Add($"Interrompu : {interrupted} (étape : {stage}) — peut être appliqué en partie ; relis-le avec get avant de le modifier de nouveau.");
        if (notAttempted.Count > 0)
        {
            lines.Add("Jamais tentés :");
            lines.AddRange(notAttempted.Select(item => "- " + item));
        }
        lines.Add("Ne renvoie pas le lot entier : un texte ajouté (append_text) serait dupliqué.");
        lines.Add("Cause : " + cause.Message);
        return new HomePartialWriteException(
            string.Join("\n", lines), "update", applied, interrupted, notAttempted, cause);
    }
}

// What a partly failed create knows about the collection membership of the
// objects it did create.
internal enum HomeMembershipState
{
    NotRequested,
    Applied,
    Unconfirmed,
}