namespace Deckle.Mail.Hub;

// The one seam between the window and whatever sorts the mail. Today a
// built-in synthetic dataset stands behind it (SampleMailHubSource); the real
// engine (Deckle.Mail) replaces it without the window changing.
//
// Two calls, because the window needs two things only:
//   Load   what was sorted before, at once: opening the window, changing page
//          or opening a subject sorts nothing again (spec, Outside the window).
//   Apply  a gesture of the person, to be carried out where the mail lives.
//          The window has already shown its result; the source owes it no
//          answer. The sample keeps gestures in memory and nothing leaves the
//          process.
public interface IMailHubSource
{
    MailHubSnapshot Load();

    void Apply(MailGesture gesture);
}

// Everything the window shows when it opens. Today is the sorting's day: the
// sample carries a fixed one so its dates read the same on every run.
public sealed record MailHubSnapshot(
    DateOnly Today,
    bool IsAnytypeConnected,
    IReadOnlyList<Mailbox> Mailboxes,
    IReadOnlyList<MailSubject> Subjects,
    IReadOnlyList<InfoLine> InfoLines,
    IReadOnlyList<ActivityEntry> Activity);

// A gesture of the person, in the words of the spec. Ids only: no text of a
// mail travels with a gesture.
public sealed record MailGesture(MailGestureKind Kind, string? TargetId = null, DateOnly? Day = null);

public enum MailGestureKind
{
    // TargetId: a subject. Day: the day of the task, null for no day.
    AddTask,
    // TargetId: a subject.
    MoveToTrash,
    // TargetId: a subject. Brings its mails back to the inbox and removes the
    // task when one was created.
    UndoGesture,
    // TargetId: a secondary information line.
    DismissInfo,
    // TargetId: an Activity line of the sorting. Brings its mail back to
    // Triage and tells the sorting it was wrong.
    BringBack,
    // Sends the set-aside mails of the day to the trash.
    EndDay,
}
