namespace Deckle.Mail;

public sealed partial record TriageState
{
    /// <summary>
    /// Add task, on a day of the menu. The task holds the sentence of the row,
    /// the text of the mail and its sender; the subject's mails go to the trash,
    /// so the inbox holds only what waits, on the phone and in the webmail too.
    /// The row greys in place and says « Task for » and the day.
    /// </summary>
    /// <param name="day">The day chosen in the menu, or null for No day yet.</param>
    public TriageChange AddTask(string subjectId, DateOnly? day, DateTimeOffset now)
    {
        if (!TryGetRow(subjectId, now, out var subject) || !subject.OffersTask)
        {
            return TriageChange.Unchanged(this);
        }

        var (next, treatment) = Treat(subject, TreatmentKind.Task, day, gestureId: LastGestureId + 1, now);
        var task = new CreateTask(subject.Id, subject.Sentence, subject.SentenceMail.From, subject.SentenceMail.Id, day)
        {
            TreatmentId = treatment.Id,
            SubjectActedOn = subject.Id,
        };
        return (next with { LastGestureId = next.LastGestureId + 1 }).Deliver([task, .. TrashIntents(subject, treatment.Id)]);
    }

    /// <summary>The trash of a row: nothing is to be done. Its mails go to the trash of their server.</summary>
    public TriageChange Trash(string subjectId, DateTimeOffset now)
    {
        if (!TryGetRow(subjectId, now, out var subject))
        {
            return TriageChange.Unchanged(this);
        }

        var (next, treatment) = Treat(subject, TreatmentKind.Trash, day: null, gestureId: LastGestureId + 1, now);
        return (next with { LastGestureId = next.LastGestureId + 1 }).Deliver(TrashIntents(subject, treatment.Id));
    }

    /// <summary>
    /// The cross of an information line. A dismissed line is treated: its mail
    /// goes to the trash as for a subject. The line leaves at once.
    /// </summary>
    public TriageChange Dismiss(string subjectId, DateTimeOffset now)
    {
        if (!Subjects.TryGetValue(subjectId, out var subject)
            || subject.Place != MailPlace.Information
            || subject.Stage != SubjectStage.Open
            || IsGuarded(subject, now))
        {
            return TriageChange.Unchanged(this);
        }

        return DismissLines([subject], now);
    }

    /// <summary>Dismiss all: every information line the page shows, as one gesture.</summary>
    public TriageChange DismissAll(MailScope scope, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var lines = Subjects.Values
            .Where(subject => subject.Place == MailPlace.Information && subject.Stage == SubjectStage.Open && scope.Contains(subject))
            .OrderBy(subject => subject.Since)
            .ToList();

        return lines.Count == 0 ? TriageChange.Unchanged(this) : DismissLines(lines, now);
    }

    /// <summary>
    /// The cross of a treated row: it removes the row. The subject stays in Activity.
    /// </summary>
    public TriageChange RemoveRow(string subjectId, DateTimeOffset now)
    {
        if (!Subjects.TryGetValue(subjectId, out var subject)
            || subject.Stage != SubjectStage.Treated
            || IsGuarded(subject, now))
        {
            return TriageChange.Unchanged(this);
        }

        return new TriageChange(WithSubject(subject with { Stage = SubjectStage.Closed, ChangedAt = now }), []);
    }

    /// <summary>
    /// Undo on a treated row or on a line of the person's part of Activity.
    /// The mails come back from the trash to the inbox, the task is removed
    /// when one was created, and the row or line gets its controls back. The
    /// line of Activity stays and says its subject is back in Triage.
    /// </summary>
    public TriageChange Undo(string subjectId, DateTimeOffset now)
    {
        if (!Subjects.TryGetValue(subjectId, out var subject)
            || IsGuarded(subject, now)
            || LiveTreatmentOf(subjectId) is not { } treatment)
        {
            return TriageChange.Unchanged(this);
        }

        return UndoTreatments([treatment], now);
    }

    /// <summary>Ctrl+Z: undoes the last gesture that is not undone yet, every subject it treated.</summary>
    public TriageChange UndoLastGesture(DateTimeOffset now)
    {
        var live = Treatments.FindAll(treatment => !treatment.Undone);
        if (live.Count == 0)
        {
            return TriageChange.Unchanged(this);
        }

        var lastGesture = live.Max(treatment => treatment.GestureId);
        return UndoTreatments([.. live.Where(treatment => treatment.GestureId == lastGesture)], now);
    }

    // A row that offers a gesture: a subject of Triage that still waits, and
    // not in the instant after a gesture changed it.
    private bool TryGetRow(string subjectId, DateTimeOffset now, out Subject subject)
    {
        if (Subjects.TryGetValue(subjectId, out var found)
            && found.Place == MailPlace.Triage
            && found.Stage == SubjectStage.Open
            && !IsGuarded(found, now))
        {
            subject = found;
            return true;
        }

        subject = null!;
        return false;
    }

    private (TriageState State, Treatment Treatment) Treat(
        Subject subject, TreatmentKind kind, DateOnly? day, int gestureId, DateTimeOffset now)
    {
        var treatment = new Treatment(LastTreatmentId + 1, gestureId, subject.Id, kind, day, now);

        // A treated row stays where it was, greyed; a dismissed line leaves at once.
        var stage = kind == TreatmentKind.Dismissal ? SubjectStage.Closed : SubjectStage.Treated;
        var next = WithSubject(subject with { Stage = stage, ChangedAt = now }) with
        {
            Treatments = Treatments.Add(treatment),
            LastTreatmentId = treatment.Id,
        };
        return (next, treatment);
    }

    private TriageChange DismissLines(IReadOnlyList<Subject> lines, DateTimeOffset now)
    {
        var gestureId = LastGestureId + 1;
        var next = this with { LastGestureId = gestureId };
        var intents = new List<MailIntent>();
        foreach (var line in lines)
        {
            (next, var treatment) = next.Treat(line, TreatmentKind.Dismissal, day: null, gestureId, now);
            intents.AddRange(TrashIntents(line, treatment.Id));
        }

        return next.Deliver(intents);
    }

    private TriageChange UndoTreatments(IReadOnlyList<Treatment> treatments, DateTimeOffset now)
    {
        var next = this;
        var intents = new List<MailIntent>();
        foreach (var treatment in treatments)
        {
            var subject = next.Subjects[treatment.SubjectId];

            // An intent still kept for an unreachable target never left: it is
            // dropped, and nothing has to be reversed on that target.
            var held = next.HeldIntents.FindAll(intent => intent.TreatmentId == treatment.Id);
            next = next with { HeldIntents = next.HeldIntents.RemoveAll(intent => intent.TreatmentId == treatment.Id) };

            if (treatment.Kind == TreatmentKind.Task && !held.Exists(intent => intent is CreateTask))
            {
                intents.Add(new RemoveTask(subject.Id) { TreatmentId = treatment.Id, SubjectActedOn = subject.Id });
            }

            intents.AddRange(RestoreIntents(subject, treatment.Id)
                .Where(restore => !held.Exists(intent => intent is MoveToTrash kept && kept.MailboxId == restore.MailboxId)));

            next = next.WithTreatment(treatment with { Undone = true })
                .WithSubject(subject with { Stage = SubjectStage.Open, ChangedAt = now });
        }

        return next.Deliver(intents);
    }
}
