namespace Deckle.Mail;

/// <summary>A line of the person's part of Activity.</summary>
/// <param name="BackInTriage">True once undone: the line says its subject is back in Triage and keeps its chevron only.</param>
public sealed record PersonActivityLine(
    int TreatmentId,
    string SubjectId,
    DateTimeOffset At,
    string Sentence,
    TreatmentResult Result,
    bool BackInTriage);

/// <summary>A line of the sorting's part of Activity: one mail, with the framed word of its reason.</summary>
/// <param name="BackInTriage">True once undone: the mail is back in Triage as a subject.</param>
public sealed record SortingActivityLine(
    string MailId,
    string SubjectId,
    DateTimeOffset At,
    string Sentence,
    SetAsideReason Reason,
    bool BackInTriage);

/// <summary>The lines of one day, newest first.</summary>
public sealed record ActivityDay<TLine>(DateOnly Day, IReadOnlyList<TLine> Lines);

/// <summary>
/// The two parts of Activity, one shown at a time: what the person did, and
/// what the sorting did alone. In each, lines are grouped by day, newest first.
/// </summary>
public static class ActivityLog
{
    /// <summary>True while there is no activity at all, by the person or the sorting: Activity is absent from the navigation.</summary>
    public static bool IsEmpty(TriageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Treatments.IsEmpty && state.SortingRecords.IsEmpty;
    }

    public static IReadOnlyList<ActivityDay<PersonActivityLine>> ByPerson(TriageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var lines = state.Treatments.Select(treatment => new PersonActivityLine(
            treatment.Id,
            treatment.SubjectId,
            treatment.At,
            state.Subjects[treatment.SubjectId].Sentence,
            state.ResultOf(treatment),
            treatment.Undone));
        return ByDay(lines, line => line.At);
    }

    public static IReadOnlyList<ActivityDay<SortingActivityLine>> BySorting(TriageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var lines = state.SortingRecords.Select(record => new SortingActivityLine(
            record.MailId,
            record.SubjectId,
            record.At,
            record.Sentence,
            record.Reason,
            record.Undone));
        return ByDay(lines, line => line.At);
    }

    // Lines come in the order they were made, oldest first. Two lines of the
    // same moment, a Dismiss all or a burst of sorting, keep that order
    // reversed, so newest first holds on a tie too.
    private static List<ActivityDay<TLine>> ByDay<TLine>(IEnumerable<TLine> lines, Func<TLine, DateTimeOffset> at) =>
        [.. lines
            .Select((line, made) => (Line: line, Made: made))
            .GroupBy(entry => MailCalendar.DayOf(at(entry.Line)))
            .OrderByDescending(day => day.Key)
            .Select(day => new ActivityDay<TLine>(
                day.Key,
                [.. day.OrderByDescending(entry => at(entry.Line)).ThenByDescending(entry => entry.Made).Select(entry => entry.Line)]))];
}
