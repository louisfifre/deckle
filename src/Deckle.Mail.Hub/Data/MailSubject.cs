namespace Deckle.Mail.Hub;

// One subject: one mail, or a run of mails about one matter. Everywhere the
// window names a matter (a row, an Activity line, an opened page), it names
// one of these.
//
// Sentence is written once, when the mail was sorted, in the language of the
// mail (Language, a BCP 47 tag the text elements carry so a screen reader
// speaks it right). Deadline exists only when the text of a mail states one;
// it orders the list and proposes the day of a task, and is never written
// apart from the sentence. Mails are newest first.
public sealed record MailSubject(
    string Id,
    string MailboxId,
    string? AliasId,
    string Sentence,
    string Language,
    DateOnly? Deadline,
    IReadOnlyList<SubjectMail> Mails,
    SubjectPlace Place,
    SubjectMark Mark = SubjectMark.None)
{
    // A paperclip shows when a mail of the subject carries a real file; an
    // image of the body (a logo, a signature) is never an attachment here.
    public bool HasAttachment => Mails.Any(m => m.Attachments.Count > 0);

    // The oldest mail: rows without a deadline come oldest first.
    public DateTimeOffset FirstReceived => Mails.Count == 0 ? DateTimeOffset.MinValue : Mails.Min(m => m.Received);

    public SubjectMail Latest => Mails[0];

    // A sender that failed the checks cannot become a task: the row offers
    // the trash only.
    public bool CanBecomeTask => Mark != SubjectMark.SenderNotVerified;
}

// Where the sorting put a subject when the window opened.
public enum SubjectPlace
{
    // In Triage: it asks something of the person.
    Waiting,
    // Treated by a gesture of the person before this window opened.
    Treated,
    // Set aside by the sorting, listed in Activity, By Deckle.
    SetAside,
}

// A state the sorting sets on a row, always shown as a framed label.
public enum SubjectMark
{
    None,
    // The sorting could not read it: images only, or encrypted.
    Unreadable,
    // Its sender failed the checks.
    SenderNotVerified,
}

// One mail of a subject. IsFromYou marks a mail the person sent from elsewhere
// in the same thread, listed under « You ». Summary is the short sentence of
// the list of an opened subject; Body is the text, whole, one paragraph each.
public sealed record SubjectMail(
    string Id,
    string SenderName,
    string SenderAddress,
    bool IsFromYou,
    DateTimeOffset Received,
    string Summary,
    IReadOnlyList<string> Body,
    IReadOnlyList<MailAttachment> Attachments);

public sealed record MailAttachment(string Name, string Kind, long SizeBytes);
