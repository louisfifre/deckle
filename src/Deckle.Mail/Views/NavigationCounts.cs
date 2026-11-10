namespace Deckle.Mail;

/// <summary>
/// The numbers one mailbox wears in the navigation. A null number shows
/// nothing: an item where nothing waits shows no number.
/// </summary>
/// <param name="Mailbox">The number on the mailbox item, or on its group header while folded.</param>
/// <param name="Inbox">The number on Inbox while the group is unfolded; null for a single item.</param>
/// <param name="Aliases">The number on each alias while the group is unfolded, by alias address.</param>
public sealed record MailboxCounts(int? Mailbox, int? Inbox, IReadOnlyDictionary<string, int?> Aliases);

/// <summary>
/// Where something waits, before the person clicks. Only subjects that wait in
/// Triage count: no number anywhere counts what was treated or set aside, nor
/// an information line, which asks nothing.
/// </summary>
public static class NavigationCounts
{
    /// <param name="folded">True while the group header of a mailbox with aliases is folded. Ignored for a single item.</param>
    public static MailboxCounts For(TriageState state, Mailbox mailbox, bool folded)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(mailbox);

        var total = Waiting(state, MailScope.Mailbox(mailbox.Id));
        var noAliases = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        if (!mailbox.IsGroup)
        {
            return new MailboxCounts(total, null, noAliases);
        }

        if (folded)
        {
            // Folded, the header wears the number of the whole mailbox, its aliases included.
            return new MailboxCounts(total, null, noAliases);
        }

        // Unfolded, the header wears none and its children wear theirs. Inbox
        // carries the total of the mailbox, the number the folded header wore
        // (spec Open 8, the option drawn in 19A and 19E): with none, a mail
        // that arrived at the main address would be counted nowhere.
        // Addresses compare without case, as mail does: Shop@ and shop@ are one
        // alias, counted once under the first spelling the mailbox lists.
        var aliases = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in mailbox.Aliases)
        {
            aliases.TryAdd(alias.Address, Waiting(state, MailScope.Alias(mailbox.Id, alias.Address)));
        }

        return new MailboxCounts(null, total, aliases);
    }

    private static int? Waiting(TriageState state, MailScope scope)
    {
        var count = state.Subjects.Values.Count(subject =>
            subject.Place == MailPlace.Triage && subject.Stage == SubjectStage.Open && scope.Contains(subject));
        return count == 0 ? null : count;
    }
}
