namespace Deckle.Mail;

public sealed partial record TriageState
{
    /// <summary>
    /// An outer layer could not reach a mailbox or Anytype. From now on, the
    /// intents for it are kept and their rows show that they wait.
    /// </summary>
    public TriageChange TargetUnreachable(MailTarget target) =>
        new(this with { UnreachableTargets = UnreachableTargets.Add(target) }, []);

    /// <summary>A target is back: the intents kept for it are released, in the order they were made.</summary>
    public TriageChange TargetReachable(MailTarget target)
    {
        var released = HeldIntents.FindAll(intent => intent.Target == target);
        var next = this with
        {
            UnreachableTargets = UnreachableTargets.Remove(target),
            HeldIntents = HeldIntents.RemoveAll(intent => intent.Target == target),
        };
        return new TriageChange(next, released);
    }

    /// <summary>
    /// An intent sent earlier did not arrive: it is kept for when its target is
    /// back, and its target counts as unreachable until then. An intent the
    /// subject no longer wants, because its gesture was undone or redone since,
    /// is dropped: sending it later would undo what the row now shows.
    /// </summary>
    public TriageChange ReportUndelivered(MailIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var next = this with { UnreachableTargets = UnreachableTargets.Add(intent.Target) };
        if (IsStillWanted(intent))
        {
            next = next with { HeldIntents = HeldIntents.Add(intent) };
        }

        return new TriageChange(next, []);
    }

    // Whether an intent still matches where its subject stands now. Mails are
    // in the trash exactly while their subject is treated or closed, and a
    // task exists exactly while the gesture that created it holds.
    private bool IsStillWanted(MailIntent intent)
    {
        if (intent.SubjectActedOn is null || !Subjects.TryGetValue(intent.SubjectActedOn, out var subject))
        {
            return true;
        }

        var live = LiveTreatmentOf(subject.Id);
        return intent switch
        {
            MoveToTrash => subject.Stage != SubjectStage.Open,
            RestoreFromTrash => subject.Stage == SubjectStage.Open,
            CreateTask => live is not null && live.Id == intent.TreatmentId,

            // RemoveTask names the subject, not one task: once a new task
            // holds the subject, removing "its task" would remove that one.
            RemoveTask => live is not { Kind: TreatmentKind.Task },
            _ => true,
        };
    }

    // Splits what a transition asks for: an intent for a reachable target goes
    // out now, one for an unreachable target is kept in the state.
    private TriageChange Deliver(IEnumerable<MailIntent> intents)
    {
        var sent = new List<MailIntent>();
        var held = HeldIntents;
        foreach (var intent in intents)
        {
            if (UnreachableTargets.Contains(intent.Target))
            {
                held = held.Add(intent);
            }
            else
            {
                sent.Add(intent);
            }
        }

        return new TriageChange(this with { HeldIntents = held }, sent);
    }

    // One intent per mailbox, since each server only moves its own mails.
    private static IEnumerable<MoveToTrash> TrashIntents(Subject subject, int? treatmentId) =>
        subject.Mails
            .GroupBy(mail => mail.MailboxId, StringComparer.Ordinal)
            .Select(group => new MoveToTrash(group.Key, [.. group.Select(mail => mail.Id)])
            {
                TreatmentId = treatmentId,
                SubjectActedOn = subject.Id,
            });

    // The reverse of TrashIntents, one intent per mailbox as well.
    private static IEnumerable<RestoreFromTrash> RestoreIntents(Subject subject, int? treatmentId) =>
        TrashIntents(subject, treatmentId).Select(trash => new RestoreFromTrash(trash.MailboxId, trash.MailIds)
        {
            TreatmentId = treatmentId,
            SubjectActedOn = subject.Id,
        });
}
