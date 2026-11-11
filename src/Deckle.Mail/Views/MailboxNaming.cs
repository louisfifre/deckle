namespace Deckle.Mail;

/// <summary>How the navigation, the breadcrumbs and Settings name a mailbox and its aliases.</summary>
public static class MailboxNaming
{
    /// <summary>The name given when the mailbox was added, or its address when it was given none.</summary>
    public static string NameOf(Mailbox mailbox)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        return string.IsNullOrWhiteSpace(mailbox.Name) ? mailbox.Address : mailbox.Name.Trim();
    }

    /// <summary>
    /// The name of an alias: the one the person gave it by hand, or else the
    /// part of its address before the @, with a capital while the mailbox's
    /// option says so (shop@ → « Shop »). With the option off, that part shows
    /// as written: the option is « capitals or not » (spec Open 18).
    /// </summary>
    public static string NameOf(Mailbox mailbox, MailAlias alias)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        ArgumentNullException.ThrowIfNull(alias);
        if (!string.IsNullOrWhiteSpace(alias.Name))
        {
            return alias.Name.Trim();
        }

        // The last @, since a quoted local part may hold one and a domain never does.
        var at = alias.Address.LastIndexOf('@');
        var localPart = at > 0 ? alias.Address[..at] : alias.Address;
        return mailbox.CapitalizesAliasNames && localPart.Length > 0
            ? char.ToUpperInvariant(localPart[0]) + localPart[1..]
            : localPart;
    }

    /// <summary>Inbox stands for the main address of a mailbox. It is not renamed.</summary>
    public static string InboxName => MailWording.Inbox;
}
