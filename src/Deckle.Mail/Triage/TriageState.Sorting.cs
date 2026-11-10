using System.Collections.Immutable;

namespace Deckle.Mail;

public sealed partial record TriageState
{
    /// <summary>
    /// Places one sorted mail: it joins the open subject of its thread or of its
    /// matter number, or starts a subject of its own. A subject a new mail joins
    /// is sorted again from that mail, so it can stay, become an information
    /// line or be set aside. Sorting sends nothing: mail set aside reaches the
    /// trash only when the person ends the day.
    /// </summary>
    /// <param name="now">When the sorting placed it: the time of its line in Activity.</param>
    public TriageChange Sort(Mail mail, MailJudgment judgment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(judgment);

        // The same mail read twice, after a reconnection for instance, is
        // already placed: sorting it again would duplicate it in its subject.
        if (Subjects.Values.Any(subject => subject.Mails.Exists(known => known.Id == mail.Id)))
        {
            return TriageChange.Unchanged(this);
        }

        var (place, reason) = MailPlacement.Place(mail, judgment, Corrections);
        var joined = SubjectJoining.FindOpenSubject(Subjects.Values, mail, judgment);
        var subject = joined is null
            ? StartSubject(mail, judgment, place, reason)
            : Resort(joined, mail, judgment, place, reason);

        var next = WithSubject(subject);
        if (place == MailPlace.SetAside)
        {
            next = next with
            {
                SortingRecords = SortingRecords.Add(
                    new SortingRecord(mail.Id, subject.Id, subject.Sentence, reason!.Value, mail.From, now)),
            };
        }

        return new TriageChange(next, []);
    }

    private static Subject StartSubject(Mail mail, MailJudgment judgment, MailPlace place, SetAsideReason? reason) =>
        new()
        {
            Id = mail.Id,
            Mails = [mail],
            Place = place,
            SetAsideReason = reason,
            Sentence = SentenceOf(mail, judgment),
            Deadline = judgment.Deadline,
            Unreadable = judgment.Unreadable,
            MatterNumbers = judgment.MatterNumbers.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
        };

    private static Subject Resort(Subject subject, Mail mail, MailJudgment judgment, MailPlace place, SetAsideReason? reason)
    {
        // A row carries the sentence that asks, even when a later mail of the
        // thread only repeats the request: the reminder says nothing new, and
        // its deadline is the one the asking mail already gave.
        var keepsRequest = judgment.RepeatsRequest && subject.Place == MailPlace.Triage;

        return subject with
        {
            Mails = subject.Mails.Add(mail),
            Place = place,
            SetAsideReason = reason,
            Sentence = keepsRequest ? subject.Sentence : SentenceOf(mail, judgment),
            Deadline = keepsRequest ? subject.Deadline : judgment.Deadline,
            Unreadable = judgment.Unreadable,
            MatterNumbers = subject.MatterNumbers.Union(judgment.MatterNumbers),
        };
    }

    // A mail the sorting could not read has its own subject line as its sentence.
    private static string SentenceOf(Mail mail, MailJudgment judgment) => judgment.Sentence ?? mail.SubjectLine;
}
