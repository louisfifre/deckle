namespace Deckle.Mail.Hub;

// The gestures of Triage, in memory. Each one changes the row in place (it
// greys where it is, with Undo and its result), writes or removes its line of
// Activity, and hands the gesture to the source. A gesture that cannot apply
// (a row already treated, a task without Anytype) returns false and changes
// nothing.
public sealed partial class TriageSession
{
    // Rows treated in this window, last gesture last: Ctrl+Z undoes the top.
    private readonly List<string> _undoable = [];

    // The first time a gesture of the person would send a mail to the trash,
    // a notice says where treated mail goes and what the provider does with
    // it; the gesture happens only if the person accepts (spec, The trash).
    // Kept for the life of the window: the sample has no store to remember it
    // across openings.
    public bool IsTrashNoticeAccepted { get; private set; }

    public void AcceptTrashNotice() => IsTrashNoticeAccepted = true;

    public bool AddTask(string subjectId, DateOnly? day)
    {
        if (!IsAnytypeConnected) return false;
        TriageRow? row = FindRow(subjectId);
        if (row is null || row.IsTreated || !row.Subject.CanBecomeTask) return false;

        Treat(row, GestureResult.Task(day));
        _source.Apply(new MailGesture(MailGestureKind.AddTask, subjectId, day));
        return true;
    }

    public bool MoveToTrash(string subjectId)
    {
        TriageRow? row = FindRow(subjectId);
        if (row is null || row.IsTreated) return false;

        Treat(row, GestureResult.Trash);
        _source.Apply(new MailGesture(MailGestureKind.MoveToTrash, subjectId));
        return true;
    }

    // Undo on the row itself: the gesture is taken back as if it had not been
    // made, so its Activity line goes too. Undo from an Activity line keeps
    // the line (TriageSession.Activity.cs).
    public bool Undo(string subjectId)
    {
        TriageRow? row = FindRow(subjectId);
        if (row is null || !row.IsTreated) return false;

        row.Result = null;
        row.ChangedAt = Now;
        if (row.ActivityId is { } activityId)
            _activity.RemoveAll(a => string.Equals(a.Id, activityId, StringComparison.Ordinal));
        row.ActivityId = null;
        _undoable.Remove(subjectId);
        _source.Apply(new MailGesture(MailGestureKind.UndoGesture, subjectId));
        Raise(SessionChange.Rows | SessionChange.Activity);
        return true;
    }

    // Ctrl+Z: the last gesture whose row is still treated and on the page.
    public bool UndoLast()
    {
        for (int i = _undoable.Count - 1; i >= 0; i--)
        {
            if (FindRow(_undoable[i]) is { IsTreated: true } row)
                return Undo(row.Id);
            _undoable.RemoveAt(i);
        }
        return false;
    }

    // The cross of a treated row: the line leaves the list.
    public bool RemoveTreatedRow(string subjectId)
    {
        TriageRow? row = FindRow(subjectId);
        if (row is null || !row.IsTreated) return false;

        _rows.Remove(row);
        _undoable.Remove(subjectId);
        Raise(SessionChange.Rows);
        return true;
    }

    // False in the instant after a gesture changed the row: the click that
    // follows is dropped.
    public bool AcceptsClick(string subjectId)
        => FindRow(subjectId) is not { } row || Now - row.ChangedAt >= ClickGuard;

    // A dismissed line is treated: its mail goes to the trash as for a subject.
    public bool DismissInfo(string infoId)
    {
        int removed = _infos.RemoveAll(i => string.Equals(i.Id, infoId, StringComparison.Ordinal));
        if (removed == 0) return false;
        _source.Apply(new MailGesture(MailGestureKind.DismissInfo, infoId));
        Raise(SessionChange.InfoLines);
        return true;
    }

    // Dismiss all clears everything under the rows: every line, and the line
    // that counts the mails set aside. Those mails are not dismissed: nothing
    // is handed to the source for them, and they go to the trash when the day
    // ends, as before. Returns the number of lines dismissed.
    public int DismissAllInfo(MailFilter filter)
    {
        List<InfoLine> lines = [.. InfoLinesIn(filter)];
        foreach (InfoLine line in lines)
        {
            _infos.Remove(line);
            _source.Apply(new MailGesture(MailGestureKind.DismissInfo, line.Id));
        }
        foreach (ActivityEntry entry in _activity)
            if (AwaitsTheTrash(entry, filter)) _setAsideSeen.Add(entry.Id);
        if (lines.Count > 0) Raise(SessionChange.InfoLines);
        return lines.Count;
    }

    // Done for today: the treated rows leave, every secondary information line
    // is dismissed, and the mails the sorting set aside go to the trash now,
    // not before (spec, The trash; Open 1 leans on this reading).
    public void EndDay()
    {
        _rows.RemoveAll(r => r.IsTreated);
        _undoable.Clear();
        foreach (InfoLine line in _infos)
            _source.Apply(new MailGesture(MailGestureKind.DismissInfo, line.Id));
        _infos.Clear();
        for (int i = 0; i < _activity.Count; i++)
            if (_activity[i].AwaitsEndOfDay)
                _activity[i] = _activity[i] with { AwaitsEndOfDay = false };
        _source.Apply(new MailGesture(MailGestureKind.EndDay));
        Raise(SessionChange.Rows | SessionChange.InfoLines | SessionChange.Activity);
    }

    private void Treat(TriageRow row, GestureResult result)
    {
        row.Result = result;
        row.ChangedAt = Now;
        MailSubject subject = row.Subject;
        var entry = new ActivityEntry(
            $"a-{Guid.NewGuid():N}", ActivityPart.ByYou, Stamp(), subject.Id, subject.MailboxId, subject.AliasId,
            subject.Sentence, subject.Language, result, Reason: null);
        _activity.Insert(0, entry);
        row.ActivityId = entry.Id;
        _undoable.Remove(row.Id);
        _undoable.Add(row.Id);
        Raise(SessionChange.Rows | SessionChange.Activity);
    }
}
