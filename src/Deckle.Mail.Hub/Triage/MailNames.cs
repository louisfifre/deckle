namespace Deckle.Mail.Hub;

// How a mailbox and an alias are named in the navigation and on their pages.
//
// A mailbox shows the name given when it was added, or its address when it
// was given none. An alias shows the name the person gave it; without one,
// and while the option of its mailbox is on, the part of its address before
// the @ with a capital (« shop@ » is « Shop »); with the option off, its
// address. Inbox stands for the main address and is not renamed.
public static class MailNames
{
    public static string Mailbox(Mailbox mailbox)
        => string.IsNullOrWhiteSpace(mailbox.GivenName) ? mailbox.Address : mailbox.GivenName.Trim();

    public static string Alias(MailAlias alias, bool namesFromAddress)
    {
        if (!string.IsNullOrWhiteSpace(alias.GivenName)) return alias.GivenName.Trim();
        return namesFromAddress ? Capitalize(LocalPart(alias.Address)) : alias.Address;
    }

    // The mark of a mailbox in the navigation: its initial alone (Open 9,
    // settled for now; an icon the person picks is Open 53).
    public static string Initial(string label)
    {
        foreach (char c in label)
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        return "?";
    }

    public static string LocalPart(string address)
    {
        int at = address.IndexOf('@');
        return at > 0 ? address[..at] : address;
    }

    private static string Capitalize(string word)
        => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
