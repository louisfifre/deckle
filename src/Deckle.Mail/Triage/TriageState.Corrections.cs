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

        // Only a subject still set aside moves: one a later mail already
        // brought to Triage, or one the person treated since, stays as it is.
        if (subject.Place != MailPlace.SetAside)
        {
            return new TriageChange(next, []);
        }

        // Set-aside mail that is closed went to the trash when the day ended,
        // so it comes back from there.
        var restore = subject.Stage == SubjectStage.Closed
            ? TrashIntents(subject, treatmentId: null).Select(trash => new RestoreFromTrash(trash.MailboxId, trash.MailIds))
            : [];

        next = next.WithSubject(subject with
        {
            Place = MailPlace.Triage,
            SetAsideReason = null,
            Stage = SubjectStage.Open,
            ChangedAt = now,
        });
        return next.Deliver(restore);
    }
}
