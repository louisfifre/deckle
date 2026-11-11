namespace Deckle.Mail.Hub;

// ── TriageSession ─────────────────────────────────────────────────────────────
//
// What the Mail window holds while it is open: the snapshot it loaded, then
// every gesture the person made since, kept in memory. Pages are views over
// it, so a row treated on Triage reads treated on its Inbox page too, and an
// opened subject shows the gesture made on its row.
//
// It lives as long as the window: closing the window drops the treated rows,
// as the spec has them leave "when the window is closed or refreshed".
//
// Pure on purpose (no WinUI type), so the behaviour a person notices, the
// order, a gesture and its undo, the counts of the navigation, is tested
// without a window. Split by responsibility: this file holds the state and
// what pages read; .Gestures, .Activity, .Mailboxes and .Problems hold the
// rest.
public sealed partial class TriageSession
{
    // A click that lands on a row in the instant after a gesture changed it
    // does nothing: a double click on a day of the menu, or on Undo, never
    // undoes what it has just done (spec, The trash).
    public static readonly TimeSpan ClickGuard = TimeSpan.FromMilliseconds(500);

    private readonly IMailHubSource _source;
    private readonly TimeProvider _clock;
    private readonly List<Mailbox> _mailboxes;
    private readonly Dictionary<string, MailSubject> _subjects;
    private readonly List<TriageRow> _rows;
    private readonly List<InfoLine> _infos;
    // Newest first, as Activity lists it.
    private readonly List<ActivityEntry> _activity;

    public TriageSession(IMailHubSource source, TimeProvider? clock = null)
    {
        _source = source;
        _clock = clock ?? TimeProvider.System;

        MailHubSnapshot snapshot = source.Load();
        Today = snapshot.Today;
        IsAnytypeConnected = snapshot.IsAnytypeConnected;
        _mailboxes = [.. snapshot.Mailboxes];
        _subjects = snapshot.Subjects.ToDictionary(s => s.Id, StringComparer.Ordinal);
        _rows = [.. TriageOrder.Sort(snapshot.Subjects.Where(s => s.Place == SubjectPlace.Waiting))
            .Select(s => new TriageRow(s))];
        _infos = [.. snapshot.InfoLines];
        _activity = [.. snapshot.Activity.OrderByDescending(a => a.At)];
    }

    // Raised after every change, on the caller's thread, with what changed.
    public event Action<SessionChange>? Changed;

    public DateOnly Today { get; }

    public bool IsAnytypeConnected { get; }

    public IReadOnlyList<Mailbox> Mailboxes => _mailboxes;

    public IReadOnlyList<TriageRow> Rows => _rows;

    public IReadOnlyList<InfoLine> InfoLines => _infos;

    public IReadOnlyList<ActivityEntry> Activity => _activity;

    // Every subject the window can open: waiting, treated or set aside.
    public IEnumerable<MailSubject> AllSubjects => _subjects.Values;

    // Activity is absent from the navigation while there is no activity at
    // all, neither by the person nor by the sorting (spec, Navigation).
    public bool HasActivity => _activity.Count > 0;

    public bool HasMailbox => _mailboxes.Count > 0;

    public Mailbox? FindMailbox(string? id)
        => id is null ? null : _mailboxes.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    public MailSubject? FindSubject(string? id)
        => id is not null && _subjects.TryGetValue(id, out MailSubject? subject) ? subject : null;

    public TriageRow? FindRow(string? subjectId)
        => subjectId is null ? null : _rows.FirstOrDefault(r => string.Equals(r.Id, subjectId, StringComparison.Ordinal));

    public IEnumerable<TriageRow> RowsIn(MailFilter filter)
        => _rows.Where(r => filter.Includes(r.Subject.MailboxId, r.Subject.AliasId));

    public IEnumerable<InfoLine> InfoLinesIn(MailFilter filter)
        => _infos.Where(i => filter.Includes(i.MailboxId, i.AliasId));

    // The number the navigation shows: subjects that wait. No number anywhere
    // counts what was treated or set aside.
    public int WaitingCount(MailFilter filter) => RowsIn(filter).Count(r => !r.IsTreated);

    // The last line under the list: the mails set aside without a line, which
    // go to the trash when the person ends the day.
    public int SetAsideCount(MailFilter filter)
        => _activity.Count(a => a.Part == ActivityPart.ByDeckle && a.AwaitsEndOfDay && !a.Undone
                                && filter.Includes(a.MailboxId, a.AliasId));

    // When every row is treated, Done for today becomes the one emphasised
    // command of the page.
    public bool IsEveryRowTreated(MailFilter filter)
    {
        bool any = false;
        foreach (TriageRow row in RowsIn(filter))
        {
            if (!row.IsTreated) return false;
            any = true;
        }
        return any;
    }

    // A treated row whose gesture cannot reach where it goes yet: its mailbox
    // cannot connect. It shows its result followed by a caution dot.
    public bool WaitsForMailbox(TriageRow row)
        => row.IsTreated && FindMailbox(row.Subject.MailboxId)?.Connection == MailboxConnection.CannotConnect;

    private DateTimeOffset Now => _clock.GetUtcNow();

    // An Activity time for a gesture made now, on the sorting's day: the
    // sample's day is fixed, the clock is not.
    private DateTimeOffset Stamp()
    {
        DateTimeOffset local = _clock.GetLocalNow();
        return new DateTimeOffset(Today.ToDateTime(TimeOnly.FromDateTime(local.DateTime)), local.Offset);
    }

    private void Raise(SessionChange change) => Changed?.Invoke(change);
}

[Flags]
public enum SessionChange
{
    None = 0,
    Rows = 1,
    InfoLines = 2,
    Activity = 4,
    Mailboxes = 8,
    Problems = 16,
}
