namespace Deckle.Llm.Rewrite;

// ─── Gate verdict ────────────────────────────────────────────────────────────
//
// What the gate hands back: an all-or-nothing acceptance plus the edit script
// that justified it. The script is not a debugging extra — it is the diff the
// offer shows "tel quel" in the inlay, and the reasons histogram the eval and
// the calibration dataset feed on. On rejection the script is still complete:
// every edit is there, the violating ones flagged, so a rejection can always
// answer "why".

/// <summary>How one edit of the script was ruled. Allowed rulings map to the
/// three framing rules; Rejected rulings are their negations.</summary>
public enum DiffEditRuling
{
    /// <summary>Token(s) identical on both sides, character for character.</summary>
    Match,

    /// <summary>Word replacement within the bounded form distance — accent
    /// restoration, capitalization, elision, phonetic re-segmentation.</summary>
    AllowedReplacement,

    /// <summary>Punctuation inserted, or a function word from the closed
    /// class.</summary>
    AllowedInsertion,

    /// <summary>An adjacent duplicate or a crutch word/phrase removed.</summary>
    AllowedDeletion,

    /// <summary>Word replaced beyond the form bound — vocabulary
    /// substitution, the edit the gate exists to block.</summary>
    RejectedReplacement,

    /// <summary>A content word introduced out of nowhere.</summary>
    RejectedInsertion,

    /// <summary>A content word dropped — neither duplicate nor crutch.</summary>
    RejectedDeletion,
}

/// <summary>One edit of the alignment script. <paramref name="Original"/> and
/// <paramref name="Rewritten"/> are the space-joined tokens of each side —
/// either may be empty for insertions/deletions.</summary>
public readonly record struct DiffEdit(DiffEditRuling Ruling, string Original, string Rewritten)
{
    public bool IsAllowed => Ruling
        is DiffEditRuling.Match
        or DiffEditRuling.AllowedReplacement
        or DiffEditRuling.AllowedInsertion
        or DiffEditRuling.AllowedDeletion;
}

/// <summary>The gate's answer for one (original, rewritten) pair.</summary>
public sealed class DiffGateVerdict
{
    public DiffGateVerdict(IReadOnlyList<DiffEdit> edits)
    {
        Edits = edits;
        bool accepted = true;
        bool identity = true;
        foreach (var edit in edits)
        {
            if (!edit.IsAllowed) accepted = false;
            if (edit.Ruling != DiffEditRuling.Match) identity = false;
        }
        Accepted = accepted;
        IsIdentity = identity;
    }

    /// <summary>All-or-nothing: true only when every edit is allowed. A
    /// rejected paragraph produces no offer at all.</summary>
    public bool Accepted { get; }

    /// <summary>True when the rewrite changed nothing — an accepted verdict
    /// with nothing to offer.</summary>
    public bool IsIdentity { get; }

    /// <summary>The full edit script, in reading order, violations included.</summary>
    public IReadOnlyList<DiffEdit> Edits { get; }
}
