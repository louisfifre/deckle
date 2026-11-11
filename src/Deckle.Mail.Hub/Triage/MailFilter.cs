namespace Deckle.Mail.Hub;

// What a page keeps of Triage: everything (Triage itself), one mailbox (its
// Inbox, or the single item of a mailbox without aliases), or one alias.
// Inbox stands for the whole mailbox, its aliases included: it is the page of
// the mailbox, not of its main address alone (spec, Navigation).
public sealed record MailFilter(string? MailboxId = null, string? AliasId = null)
{
    public static MailFilter All { get; } = new();

    public static MailFilter Mailbox(string mailboxId) => new(mailboxId);

    public static MailFilter Alias(string mailboxId, string aliasId) => new(mailboxId, aliasId);

    public bool IsAll => MailboxId is null;

    public bool Includes(string mailboxId, string? aliasId)
        => MailboxId is null
           || (string.Equals(MailboxId, mailboxId, StringComparison.Ordinal)
               && (AliasId is null || string.Equals(AliasId, aliasId, StringComparison.Ordinal)));
}
