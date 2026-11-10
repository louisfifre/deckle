namespace Deckle.Mail;

public sealed partial record TriageState
{
    /// <summary>
    /// The command that ends the day. It removes the rows already treated and
    /// dismisses every information line, as Dismiss all does. Mail the sorting
    /// set aside goes to the trash now, not before: the trash stays a gesture
    /// of the person (spec Open 1, the reading the spec currently writes).
    /// </summary>
    public TriageChange EndDay(DateTimeOffset now)
    {
        var next = CloseTreatedRows(_ => true);

        var dismissed = next.DismissAll(MailScope.All, now);
        if (!dismissed.IsUnchanged)
        {
            next = dismissed.State;
        }

        var setAside = next.Subjects.Values
            .Where(subject => subject.Place == MailPlace.SetAside && subject.Stage == SubjectStage.Open)
            .OrderBy(subject => subject.Since)
            .ToList();

        var trash = new List<MailIntent>();
        foreach (var subject in setAside)
        {
            next = next.WithSubject(subject with { Stage = SubjectStage.Closed });
            trash.AddRange(TrashIntents(subject, treatmentId: null));
        }

        var delivered = next.Deliver(trash);
        return delivered with { Intents = [.. dismissed.Intents, .. delivered.Intents] };
    }

    /// <summary>The window was closed or refreshed: every treated row leaves.</summary>
    public TriageChange RemoveTreatedRows() => new(CloseTreatedRows(_ => true), []);

    /// <summary>The treated rows older than the fixed lifetime leave by themselves; no short timer removes a row.</summary>
    public TriageChange RemoveExpiredRows(DateTimeOffset now) =>
        new(CloseTreatedRows(treatment => now - treatment.At >= Options.TreatedRowLifetime), []);

    private TriageState CloseTreatedRows(Func<Treatment, bool> leaves)
    {
        var next = this;
        foreach (var subject in Subjects.Values.Where(subject => subject.Stage == SubjectStage.Treated))
        {
            if (LiveTreatmentOf(subject.Id) is { } treatment && leaves(treatment))
            {
                next = next.WithSubject(subject with { Stage = SubjectStage.Closed });
            }
        }

        return next;
    }
}
