using System.Globalization;

namespace Deckle.Mail.Hub;

// The search of the title bar: the senders, the sentences and the text of the
// mails of every mailbox, whatever became of their subject. It answers in a
// menu under the field; no page lists results (spec, The window).
//
// A line gives the sentence, where the subject stands and its date (Open 26
// leaves what a line shows open; this is the form frame 5.4 draws).
// Matching ignores case and accents, so « echeance » finds « échéance ».
public static class MailSearch
{
    public const int MaxHits = 6;

    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static IReadOnlyList<MailSearchHit> Find(TriageSession session, string query)
    {
        string needle = query.Trim();
        if (needle.Length < 2) return [];

        return session.AllSubjects
            .Where(s => Matches(s, needle))
            .OrderByDescending(s => s.Latest.Received)
            .Take(MaxHits)
            .Select(s => new MailSearchHit(s.Id, s.Sentence, s.Language, PlaceOf(session, s),
                DateOnly.FromDateTime(s.Latest.Received.DateTime)))
            .ToList();
    }

    private static bool Matches(MailSubject subject, string needle)
    {
        if (Contains(subject.Sentence, needle)) return true;
        foreach (SubjectMail mail in subject.Mails)
        {
            if (Contains(mail.SenderName, needle) || Contains(mail.SenderAddress, needle)) return true;
            foreach (string paragraph in mail.Body)
                if (Contains(paragraph, needle)) return true;
        }
        return false;
    }

    private static bool Contains(string text, string needle) => Compare.IndexOf(text, needle, Loose) >= 0;

    private static MailSearchPlace PlaceOf(TriageSession session, MailSubject subject)
    {
        if (session.FindRow(subject.Id) is { } row)
            return row.Result is { } result ? new MailSearchPlace(result, null) : new MailSearchPlace(null, null);

        ActivityEntry? last = session.Activity.FirstOrDefault(a => string.Equals(a.SubjectId, subject.Id, StringComparison.Ordinal) && !a.Undone);
        return last switch
        {
            { Gesture: { } gesture } => new MailSearchPlace(gesture, null),
            { Reason: { } reason } => new MailSearchPlace(null, reason),
            _ => new MailSearchPlace(null, null),
        };
    }
}

// Gesture set: treated that way. Reason set: set aside for it. Both null: it
// waits in Triage.
public sealed record MailSearchPlace(GestureResult? Gesture, SetAsideReason? Reason)
{
    public bool IsWaiting => Gesture is null && Reason is null;
}

public sealed record MailSearchHit(string SubjectId, string Sentence, string Language, MailSearchPlace Place, DateOnly Date);
