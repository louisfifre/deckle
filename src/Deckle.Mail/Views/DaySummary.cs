namespace Deckle.Mail;

/// <summary>
/// What the person did today, gesture by gesture, as the summary of the day
/// counts it: « Today · 2 tasks · 2 moved to trash · 3 dismissed ». A gesture
/// undone since counts no more.
/// </summary>
public sealed record DaySummary(int Tasks, int MovedToTrash, int Dismissed)
{
    public static DaySummary For(TriageState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        var today = MailCalendar.Today(now);
        var done = state.Treatments
            .Where(treatment => !treatment.Undone && MailCalendar.DayOf(treatment.At) == today)
            .ToList();

        return new DaySummary(
            done.Count(treatment => treatment.Kind == TreatmentKind.Task),
            done.Count(treatment => treatment.Kind == TreatmentKind.Trash),
            done.Count(treatment => treatment.Kind == TreatmentKind.Dismissal));
    }
}
