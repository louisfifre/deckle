namespace Deckle.Mail;

/// <summary>The gesture that treated a subject.</summary>
public enum TreatmentKind
{
    /// <summary>Add task, on a day of the menu.</summary>
    Task,

    /// <summary>The trash of a row.</summary>
    Trash,

    /// <summary>The cross of an information line, Dismiss all, or the end of the day.</summary>
    Dismissal,
}

/// <summary>
/// One subject treated by one gesture: a line of the person's part of Activity.
/// A gesture that treats several subjects at once, Dismiss all, gives one
/// treatment per subject under the same <see cref="GestureId"/>.
/// </summary>
public sealed record Treatment(
    int Id,
    int GestureId,
    string SubjectId,
    TreatmentKind Kind,
    DateOnly? TaskDay,
    DateTimeOffset At)
{
    /// <summary>True once undone: the line stays and says its subject is back in Triage.</summary>
    public bool Undone { get; init; }
}

/// <summary>What a treated row or a line of Activity says happened.</summary>
/// <param name="TaskDay">The day of the task, for <see cref="TreatmentKind.Task"/>; null for a task with no day.</param>
/// <param name="Waiting">True while the gesture waits for an unreachable mailbox or Anytype: the caution dot.</param>
public sealed record TreatmentResult(TreatmentKind Kind, DateOnly? TaskDay, bool Waiting);
