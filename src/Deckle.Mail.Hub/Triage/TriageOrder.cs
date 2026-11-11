namespace Deckle.Mail.Hub;

// The order of the list: what presses comes first. The subjects with a
// deadline, the nearest first, then the others, the oldest first (spec, What a
// row says; Open 5 leans on this reading). Applied once, when the window
// opens: a row never moves after that, so a gesture or a subject brought back
// never shifts the row under the person's hand.
public static class TriageOrder
{
    public static IReadOnlyList<MailSubject> Sort(IEnumerable<MailSubject> subjects)
        => subjects
            .OrderBy(s => s.Deadline is null ? 1 : 0)
            .ThenBy(s => s.Deadline ?? DateOnly.MaxValue)
            .ThenBy(s => s.FirstReceived)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
}
