namespace Deckle.Mail.Hub;

// Activity: its two parts, grouped by day, and Undo on a line.
public sealed partial class TriageSession
{
    public IReadOnlyList<ActivityDay> ActivityDays(ActivityPart part)
        => _activity
            .Where(a => a.Part == part)
            .GroupBy(a => DateOnly.FromDateTime(a.At.DateTime))
            .OrderByDescending(g => g.Key)
            .Select(g => new ActivityDay(g.Key, [.. g.OrderByDescending(a => a.At)]))
            .ToList();

    public ActivityEntry? FindActivity(string? id)
        => id is null ? null : _activity.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    // Undo on an Activity line brings its subject back to Triage, whatever it
    // was before, and the line stays, saying so. A subject already back as a
    // row keeps that row; one that left the list comes back at its end, so no
    // row already shown moves.
    public bool UndoActivity(string entryId)
    {
        int index = _activity.FindIndex(a => string.Equals(a.Id, entryId, StringComparison.Ordinal));
        if (index < 0 || _activity[index].Undone) return false;
        ActivityEntry entry = _activity[index];

        _activity[index] = entry with { Undone = true, AwaitsEndOfDay = false };

        if (FindRow(entry.SubjectId) is { } row)
        {
            row.Result = null;
            row.ActivityId = null;
            row.ChangedAt = Now;
            _undoable.Remove(row.Id);
        }
        else if (FindSubject(entry.SubjectId) is { } subject)
        {
            MailSubject waiting = subject with { Place = SubjectPlace.Waiting };
            _subjects[subject.Id] = waiting;
            _rows.Add(new TriageRow(waiting) { ChangedAt = Now });
        }

        _source.Apply(new MailGesture(
            entry.Part == ActivityPart.ByDeckle ? MailGestureKind.BringBack : MailGestureKind.UndoGesture,
            entry.Part == ActivityPart.ByDeckle ? entry.Id : entry.SubjectId));
        Raise(SessionChange.Rows | SessionChange.Activity);
        return true;
    }
}

public sealed record ActivityDay(DateOnly Day, IReadOnlyList<ActivityEntry> Entries);
