namespace Deckle.Mail.Hub;

// One row of Triage for the life of the window. A treated row stays where it
// was, greyed, with Undo and its result, until the person clicks its cross or
// ends the day; closing the window drops it (the session goes with the window).
public sealed class TriageRow
{
    internal TriageRow(MailSubject subject) => Subject = subject;

    public MailSubject Subject { get; internal set; }

    public string Id => Subject.Id;

    // What the gesture did; null while the subject waits.
    public GestureResult? Result { get; internal set; }

    public bool IsTreated => Result is not null;

    // When the row last changed state, for the instant guard
    // (TriageSession.AcceptsClick).
    internal DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.MinValue;

    // The Activity line its gesture wrote, removed again by Undo on the row.
    internal string? ActivityId { get; set; }
}
