namespace Deckle.Mail;

/// <summary>
/// Where an intent has to go: one mailbox's server, or Anytype. A gesture
/// whose target cannot be reached is kept and marked as waiting until it is back.
/// </summary>
public readonly record struct MailTarget
{
    private MailTarget(string? mailboxId) => MailboxId = mailboxId;

    /// <summary>The mailbox whose server receives the intent, or null for Anytype.</summary>
    public string? MailboxId { get; }

    public bool IsAnytype => MailboxId is null;

    public static MailTarget Anytype => default;

    public static MailTarget Mailbox(string mailboxId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxId);
        return new MailTarget(mailboxId);
    }
}

/// <summary>
/// Something a gesture asks the outside world to do. The core only describes
/// it; an outer layer sends it to the mail server or to Anytype.
/// </summary>
public abstract record MailIntent
{
    public abstract MailTarget Target { get; }

    /// <summary>The treatment that asked for it, so a held intent marks the right row as waiting.</summary>
    internal int? TreatmentId { get; init; }

    /// <summary>The subject it acts on, so a late report of failure can be checked against where the subject is now.</summary>
    internal string? SubjectActedOn { get; init; }
}

/// <summary>Move these mails of one mailbox to the trash of its server.</summary>
public sealed record MoveToTrash(string MailboxId, IReadOnlyList<string> MailIds) : MailIntent
{
    public override MailTarget Target => MailTarget.Mailbox(MailboxId);
}

/// <summary>Bring these mails of one mailbox back from the trash of its server to its inbox.</summary>
public sealed record RestoreFromTrash(string MailboxId, IReadOnlyList<string> MailIds) : MailIntent
{
    public override MailTarget Target => MailTarget.Mailbox(MailboxId);
}

/// <summary>
/// Create the task of a subject in Anytype. It holds the sentence of the row,
/// the text of the mail and its sender; the outer layer reads the text of
/// <paramref name="MailId"/>, since the core never holds a mail's body.
/// </summary>
/// <param name="Day">The day of the task, or null for a task with no day yet.</param>
public sealed record CreateTask(string SubjectId, string Sentence, MailSender Sender, string MailId, DateOnly? Day) : MailIntent
{
    public override MailTarget Target => MailTarget.Anytype;
}

/// <summary>Remove the task created for a subject, when its gesture is undone.</summary>
public sealed record RemoveTask(string SubjectId) : MailIntent
{
    public override MailTarget Target => MailTarget.Anytype;
}
