using System.Collections.Immutable;

namespace Deckle.Mail;

/// <summary>
/// Everything Deckle Mail knows in memory: the subjects, what the person did,
/// what the sorting did alone, its corrections, and the intents that wait for
/// an unreachable target. Immutable: each transition returns a new state with
/// the intents it produced, and never touches a server, a clock or a disk.
/// </summary>
/// <remarks>
/// The transitions live in partial files named for their responsibility:
/// sorting, gestures, the end of the day, corrections and delivery.
/// </remarks>
public sealed partial record TriageState
{
    public static TriageState Empty { get; } = new();

    public TriageOptions Options { get; init; } = new();

    public ImmutableDictionary<string, Subject> Subjects { get; init; } =
        ImmutableDictionary.Create<string, Subject>(StringComparer.Ordinal);

    /// <summary>What the person did, oldest first.</summary>
    public ImmutableList<Treatment> Treatments { get; init; } = [];

    /// <summary>What the sorting set aside on its own, oldest first.</summary>
    public ImmutableList<SortingRecord> SortingRecords { get; init; } = [];

    public ImmutableHashSet<SortingCorrection> Corrections { get; init; } = [];

    /// <summary>The targets an outer layer reported unreachable.</summary>
    public ImmutableHashSet<MailTarget> UnreachableTargets { get; init; } = [];

    /// <summary>Intents kept until their target is back, in the order they were made.</summary>
    public ImmutableList<MailIntent> HeldIntents { get; init; } = [];

    private int LastGestureId { get; init; }

    private int LastTreatmentId { get; init; }

    /// <summary>The result a treatment shows: its gesture, its day, and whether it still waits for its target.</summary>
    public TreatmentResult ResultOf(Treatment treatment)
    {
        ArgumentNullException.ThrowIfNull(treatment);
        return new TreatmentResult(
            treatment.Kind,
            treatment.TaskDay,
            HeldIntents.Exists(intent => intent.TreatmentId == treatment.Id));
    }

    /// <summary>The treatment that currently holds the subject, or null when none does.</summary>
    public Treatment? LiveTreatmentOf(string subjectId) =>
        Treatments.FindLast(treatment => treatment.SubjectId == subjectId && !treatment.Undone);

    private TriageState WithSubject(Subject subject) =>
        this with { Subjects = Subjects.SetItem(subject.Id, subject) };

    private TriageState WithTreatment(Treatment treatment) =>
        this with { Treatments = Treatments.Replace(Treatments.Single(t => t.Id == treatment.Id), treatment) };

    // A click that lands on a row in the instant after a gesture changed it
    // does nothing, so the second click of a double click never undoes the
    // first. Ctrl+Z is not a click and is not guarded.
    private bool IsGuarded(Subject subject, DateTimeOffset now) =>
        subject.ChangedAt is { } changedAt && now - changedAt < Options.ClickGuard;
}
