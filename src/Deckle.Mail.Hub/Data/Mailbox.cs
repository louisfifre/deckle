namespace Deckle.Mail.Hub;

// A mailbox the person added, as the window shows it and its Settings page
// edits it. Immutable: the session replaces a record with `with` when the
// person renames it or turns an option, so a page holding the old one never
// sees it change under it.
//
// GivenName is null when the person gave none: the window then shows the
// address (MailNames.Mailbox). ShownAsOf is the last moment its mail was read;
// a mailbox that cannot connect shows its mail as of then.
public sealed record Mailbox(
    string Id,
    string Address,
    string? GivenName,
    IReadOnlyList<MailAlias> Aliases,
    MailboxConnection Connection,
    bool HasSavedPassword,
    MailServer Server,
    bool SortsByAlias,
    bool NamesAliasesFromAddress,
    DateTimeOffset ShownAsOf)
{
    // A mailbox is a group in the navigation only while it sorts by alias and
    // has an alias to sort by; otherwise it is a single item.
    public bool IsGroup => SortsByAlias && Aliases.Count > 0;
}

public enum MailboxConnection
{
    Connected,
    CannotConnect,
}

// Where the mail of a mailbox is read. Provider is the company's name, shown
// beside the host ("Infomaniak · mail.infomaniak.com"). TrashDays is how long
// the provider keeps a mail in its trash before deleting it: the first trash
// notice says it.
public sealed record MailServer(string Provider, string Host, int Port, int TrashDays = 30);

// An address that sends to a mailbox. GivenName is the name the person typed;
// null lets the window name it from its address.
public sealed record MailAlias(string Id, string Address, string? GivenName = null);
