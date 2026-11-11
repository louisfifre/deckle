using System.Collections.Immutable;

namespace Deckle.Mail;

/// <summary>Where a subject stands in the life of its row or line.</summary>
public enum SubjectStage
{
    /// <summary>It waits in its place: a row of Triage, an information line, or mail set aside. New mail can join it.</summary>
    Open,

    /// <summary>A gesture treated it: the row greys in place with Undo and its result. New mail no longer joins it.</summary>
    Treated,

    /// <summary>It left the page: its row or line was removed, or the day was ended. It lives on in Activity.</summary>
    Closed,
}

/// <summary>
/// One mail, or a run of mails about one matter. Mails of one subject are one
/// object everywhere: one row, one line, one opened page.
/// </summary>
public sealed record Subject
{
    /// <summary>The message id of the subject's first mail, so the identity survives a reload of the same mails.</summary>
    public required string Id { get; init; }

    /// <summary>Its mails, in the order they were sorted. Never empty.</summary>
    public required ImmutableList<Mail> Mails { get; init; }

    public required MailPlace Place { get; init; }

    /// <summary>Set while the subject is set aside.</summary>
    public SetAsideReason? SetAsideReason { get; init; }

    /// <summary>The sentence of its row or line: the sentence that asks, when the subject asks.</summary>
    public required string Sentence { get; init; }

    /// <summary>
    /// The mail whose sentence the row shows: the asking mail after a mere
    /// reminder, else the latest. A task carries this mail's text and sender,
    /// so the task and the row it came from speak of the same mail.
    /// </summary>
    public required Mail SentenceMail { get; init; }

    /// <summary>The deadline the asking mail states, or null.</summary>
    public Deadline? Deadline { get; init; }

    /// <summary>Set when the sorting could not read its latest mail: a framed label on the row.</summary>
    public UnreadableReason? Unreadable { get; init; }

    public SubjectStage Stage { get; init; } = SubjectStage.Open;

    /// <summary>The matter numbers its mails carry, each of which joins a later mail to it.</summary>
    internal ImmutableHashSet<string> MatterNumbers { get; init; } = ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>When a gesture last changed the row, for the click that lands in the instant after.</summary>
    internal DateTimeOffset? ChangedAt { get; init; }

    /// <summary>The mail that sorted the subject last.</summary>
    public Mail Latest => Mails[^1];

    /// <summary>When the subject began: the arrival of its first mail. The list puts the oldest first.</summary>
    public DateTimeOffset Since => Mails[0].ReceivedAt;

    /// <summary>False when the sender of its latest mail failed the checks: a framed « Sender not verified » label.</summary>
    public bool SenderVerified => Latest.SenderVerified;

    /// <summary>
    /// The sender as the row shows it: the name the sender gave itself, or its
    /// address when it gave none or when it failed the checks, since that name
    /// is exactly what an impostor chooses.
    /// </summary>
    public string ShownSender =>
        SenderVerified && !string.IsNullOrWhiteSpace(Latest.From.Name) ? Latest.From.Name : Latest.From.Address;

    /// <summary>True when a mail of the subject carries a real file: the paperclip.</summary>
    public bool HasAttachment => Mails.Exists(mail => mail.HasAttachment);

    /// <summary>
    /// An unverified sender offers the trash only, never a task: a task would
    /// carry a request nobody can vouch for into the person's planning.
    /// </summary>
    public bool OffersTask => SenderVerified;
}
