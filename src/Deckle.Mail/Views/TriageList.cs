namespace Deckle.Mail;

/// <summary>One row of Triage, waiting or treated.</summary>
/// <param name="Unreadable">The framed label of a mail the sorting could not read, or null.</param>
/// <param name="SenderVerified">False shows the framed « Sender not verified » label.</param>
/// <param name="CanAddTask">
/// False leaves the task column empty, so the trash stands where every other
/// trash stands: the sender is not verified, or Anytype is not connected.
/// </param>
/// <param name="ProposedDay">The day Add task carries, or null when the mail states no deadline.</param>
/// <param name="Result">The result of the gesture that treated the row, or null while it waits.</param>
public sealed record TriageRow(
    string SubjectId,
    string Sentence,
    bool HasAttachment,
    UnreadableReason? Unreadable,
    bool SenderVerified,
    bool CanAddTask,
    DateOnly? ProposedDay,
    TreatmentResult? Result);

/// <summary>One secondary information line: its sentence, and its sender in grey right after it.</summary>
public sealed record InformationLine(string SubjectId, string Sentence, string Sender);

/// <summary>What the Triage page, an Inbox page or an alias page lists.</summary>
public static class TriageList
{
    /// <summary>
    /// One row per subject in a single list, every mailbox mixed, none folded
    /// away. What presses comes first: the subjects with a deadline, the
    /// nearest first, then the others, the oldest first (spec Open 5, the
    /// order the spec writes). A treated row keeps its place: its keys do not
    /// change when it greys.
    /// </summary>
    /// <param name="tasksAvailable">False when Anytype is not connected: Add task is absent from every row.</param>
    public static IReadOnlyList<TriageRow> Rows(TriageState state, MailScope scope, DateTimeOffset now, bool tasksAvailable)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scope);
        var today = MailCalendar.Today(now);

        return state.Subjects.Values
            .Where(subject => subject.Place == MailPlace.Triage && subject.Stage != SubjectStage.Closed && scope.Contains(subject))
            .OrderBy(subject => subject.Deadline is null)
            .ThenBy(subject => subject.Deadline)
            .ThenBy(subject => subject.Since)
            .ThenBy(subject => subject.Id, StringComparer.Ordinal)
            .Select(subject => new TriageRow(
                subject.Id,
                subject.Sentence,
                subject.HasAttachment,
                subject.Unreadable,
                subject.SenderVerified,
                subject.OffersTask && tasksAvailable,
                TaskDayProposal.For(subject.Deadline, today),
                ResultOf(state, subject)))
            .ToList();
    }

    /// <summary>
    /// The secondary information lines under the list, in the order they
    /// arrived. The spec gives them no order of their own.
    /// </summary>
    public static IReadOnlyList<InformationLine> InformationLines(TriageState state, MailScope scope)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scope);

        return state.Subjects.Values
            .Where(subject => subject.Place == MailPlace.Information && subject.Stage == SubjectStage.Open && scope.Contains(subject))
            .OrderBy(subject => subject.Since)
            .ThenBy(subject => subject.Id, StringComparer.Ordinal)
            .Select(subject => new InformationLine(subject.Id, subject.Sentence, subject.ShownSender))
            .ToList();
    }

    /// <summary>
    /// The number of mails set aside and not yet gone to the trash: the last
    /// line of Triage, which leads to the sorting's part of Activity. It counts
    /// mails, not subjects.
    /// </summary>
    public static int SetAsideCount(TriageState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // The count leads to the sorting's part of Activity, so it counts that
        // part's lines, not the mails of the subject: a mail that asked before
        // a reply set its subject aside was never set aside itself.
        return state.SortingRecords.Count(record =>
            !record.Undone
            && state.Subjects[record.SubjectId] is { Place: MailPlace.SetAside, Stage: SubjectStage.Open });
    }

    private static TreatmentResult? ResultOf(TriageState state, Subject subject) =>
        subject.Stage == SubjectStage.Treated && state.LiveTreatmentOf(subject.Id) is { } treatment
            ? state.ResultOf(treatment)
            : null;
}
