namespace Deckle.Mail.Hub;

// One line of Activity: a gesture of the person (ByYou) or a mail the
// sorting set aside alone (ByDeckle). A line that was undone stays and says
// its subject is back in Triage.
//
// AwaitsEndOfDay marks a set-aside mail that has not gone to the trash yet:
// it goes when the person ends the day, and Triage counts these in its last
// line.
public sealed record ActivityEntry(
    string Id,
    ActivityPart Part,
    DateTimeOffset At,
    string SubjectId,
    string MailboxId,
    string? AliasId,
    string Sentence,
    string Language,
    GestureResult? Gesture,
    SetAsideReason? Reason,
    bool Undone = false,
    bool AwaitsEndOfDay = false);

public enum ActivityPart
{
    ByYou,
    ByDeckle,
}

// Why the sorting set a mail aside. Shown as a framed word.
public enum SetAsideReason
{
    Newsletter,
    Promotion,
    Receipt,
    NoActionNeeded,
}

// What a gesture of the person did: a task on a day (or no day), or the trash.
public sealed record GestureResult(GestureKind Kind, DateOnly? TaskDay = null)
{
    public static GestureResult Trash { get; } = new(GestureKind.Trash);

    public static GestureResult Task(DateOnly? day) => new(GestureKind.Task, day);
}

public enum GestureKind
{
    Task,
    Trash,
}
