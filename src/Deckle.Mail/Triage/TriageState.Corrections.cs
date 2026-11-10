namespace Deckle.Mail;

public sealed partial record TriageState
{
    /// <summary>
    /// Undo on a line of the sorting's part of Activity. The mail comes back to
    /// Triage as a subject, whatever it was before, and the sorting learns it
    /// was wrong: the next mails of that kind from that sender wait in Triage.
    /// The line stays and says its subject is back in Triage.
    /// </summary>
    public TriageChange UndoSorting(string mailId, DateTimeOffset now)
    {
        var record = SortingRecords.FindLast(line => line.MailId == mailId && !line.Undone);
        if (record is null || !Subjects.TryGetValue(record.SubjectId, out var subject))
        {
            return TriageChange.Unchanged(this);
        }

        var next = this with
        {
            SortingRecords = SortingRecords.Replace(record, record with { Undone = true }),
            Corrections = Corrections.Add(SortingCorrection.For(record.Sender.Address, record.Reason)),
        };

        // Since it was set aside, the subject may have become anything: a row
        // the person treated, an information line they dismissed. A gesture of
        // theirs that still holds it is undone first, which brings its mails
        // back from the trash and removes its task, so the line never claims a
        // return that did not happen.
        var intents = new List<MailIntent>();
        if (next.LiveTreatmentOf(subject.Id) is { } treatment)
        {
            var undone = next.UndoTreatments([treatment], now);
            next = undone.State;
            intents.AddRange(undone.Intents);
        }
        else if (subject.Stage == SubjectStage.Closed)
        {
            // Closed with no gesture of the person: set-aside mail the end of
            // the day sent to the trash.
            var restored = next.Deliver(TrashIntents(subject, treatmentId: null)
                .Select(trash => new RestoreFromTrash(trash.MailboxId, trash.MailIds)));
            next = restored.State;
            intents.AddRange(restored.Intents);
        }

        next = next.WithSubject(next.Subjects[subject.Id] with
        {
            Place = MailPlace.Triage,
            SetAsideReason = null,
            Stage = SubjectStage.Open,
            ChangedAt = now,
        });
        return new TriageChange(next, intents);
    }
}
