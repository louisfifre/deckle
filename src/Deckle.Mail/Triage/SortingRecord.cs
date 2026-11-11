namespace Deckle.Mail;

/// <summary>
/// One mail the sorting set aside on its own: a line of the sorting's part of
/// Activity. Each mail has its own line, even several from one sender.
/// </summary>
/// <param name="Sentence">The sentence the sorting wrote for this mail.</param>
/// <param name="At">When the sorting set it aside.</param>
public sealed record SortingRecord(
    string MailId,
    string SubjectId,
    string Sentence,
    SetAsideReason Reason,
    MailSender Sender,
    DateTimeOffset At)
{
    /// <summary>True once undone: the mail is back in Triage as a subject.</summary>
    public bool Undone { get; init; }
}
