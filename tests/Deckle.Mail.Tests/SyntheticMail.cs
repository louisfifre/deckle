namespace Deckle.Mail.Tests;

/// <summary>
/// Synthetic mailboxes and mails for the suite. No real mail and no personal
/// data: addresses use example.com and example.com, and the sentences reuse
/// the spec's own examples.
/// </summary>
internal static class SyntheticMail
{
    /// <summary>Tuesday 10 November 2026, 09:00, Paris winter time.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(1));

    public static readonly DateOnly Today = new(2026, 11, 10);

    public static readonly Mailbox Perso = new("perso", "moi@example.com", "Perso")
    {
        Aliases = [new MailAlias("shop@example.com"), new MailAlias("facture@example.com")],
    };

    public static readonly Mailbox Maison = new("maison", "maison@example.com");

    private static int s_sequence;

    /// <summary>A mail to Perso's main address, received <paramref name="minutesAgo"/> before <see cref="Now"/>.</summary>
    public static Mail To(
        string deliveredTo = "moi@example.com",
        string from = "service@example.com",
        int minutesAgo = 60,
        string mailboxId = "perso",
        string? id = null) =>
        new(
            id ?? $"<{Interlocked.Increment(ref s_sequence)}@example.com>",
            mailboxId,
            deliveredTo,
            new MailSender(from, "Service"),
            Now.AddMinutes(-minutesAgo),
            "Subject line");

    /// <summary>Sorts the mails in order, each with its judgment, and returns the final state.</summary>
    public static TriageState Sorted(params (Mail Mail, MailJudgment Judgment)[] mails)
    {
        var state = TriageState.Empty;
        foreach (var (mail, judgment) in mails)
        {
            state = state.Sort(mail, judgment, Now).State;
        }

        return state;
    }

    public static IReadOnlyList<TriageRow> Rows(TriageState state, MailScope? scope = null) =>
        TriageList.Rows(state, scope ?? MailScope.All, Now, tasksAvailable: true);

    public static Deadline On(int month, int day, int? hour = null) =>
        new(new DateOnly(2026, month, day), hour is { } h ? new TimeOnly(h, 0) : null);
}
