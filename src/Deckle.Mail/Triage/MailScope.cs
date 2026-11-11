namespace Deckle.Mail;

/// <summary>
/// What a page keeps: every mailbox (Triage), one mailbox (its Inbox page), or
/// one alias. A mailbox or an alias only filters Triage.
/// </summary>
public sealed record MailScope
{
    private MailScope(string? mailboxId, string? aliasAddress)
    {
        MailboxId = mailboxId;
        AliasAddress = aliasAddress;
    }

    public string? MailboxId { get; }

    public string? AliasAddress { get; }

    /// <summary>Triage: every mailbox mixed.</summary>
    public static MailScope All { get; } = new(null, null);

    /// <summary>The Inbox page, or the page of a mailbox with no alias: the whole mailbox, aliases included.</summary>
    public static MailScope Mailbox(string mailboxId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxId);
        return new MailScope(mailboxId, null);
    }

    /// <summary>The page of one alias: what arrived through that address.</summary>
    public static MailScope Alias(string mailboxId, string aliasAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mailboxId);
        ArgumentException.ThrowIfNullOrWhiteSpace(aliasAddress);
        return new MailScope(mailboxId, aliasAddress);
    }

    /// <summary>
    /// A subject belongs to a page when one of its mails does. A thread whose
    /// mails came through two aliases shows on both pages, as it counts on both.
    /// </summary>
    public bool Contains(Subject subject) => subject.Mails.Exists(Contains);

    private bool Contains(Mail mail) =>
        (MailboxId is null || mail.MailboxId == MailboxId)
        && (AliasAddress is null || string.Equals(mail.DeliveredTo, AliasAddress, StringComparison.OrdinalIgnoreCase));
}
