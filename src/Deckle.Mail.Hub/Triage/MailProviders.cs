namespace Deckle.Mail.Hub;

// The mail server of an address, when its provider is known: Add mailbox
// fills the server settings by itself and says « Found for Infomaniak »; an
// unknown provider leaves them for the person (frame 1.4). A small table on
// purpose: the engine brings the real discovery.
public static class MailProviders
{
    private static readonly Dictionary<string, MailServer> ByDomain = new(StringComparer.OrdinalIgnoreCase)
    {
        // The spec's examples live on exemple.fr, an Infomaniak mailbox.
        ["exemple.fr"] = new MailServer("Infomaniak", "mail.infomaniak.com", 993, TrashDays: 30),
        ["ik.me"] = new MailServer("Infomaniak", "mail.infomaniak.com", 993, TrashDays: 30),
        ["etik.com"] = new MailServer("Infomaniak", "mail.infomaniak.com", 993, TrashDays: 30),
        ["gmail.com"] = new MailServer("Google", "imap.gmail.com", 993, TrashDays: 30),
        ["outlook.com"] = new MailServer("Microsoft", "outlook.office365.com", 993, TrashDays: 30),
        ["hotmail.com"] = new MailServer("Microsoft", "outlook.office365.com", 993, TrashDays: 30),
    };

    public static MailServer? Find(string address)
    {
        int at = address.LastIndexOf('@');
        if (at < 1 || at == address.Length - 1) return null;
        return ByDomain.TryGetValue(address[(at + 1)..].Trim(), out MailServer? server) ? server : null;
    }

    // Enough of an address to try it: something, an @, a domain with a dot.
    public static bool LooksLikeAddress(string address)
    {
        string a = address.Trim();
        int at = a.IndexOf('@');
        return at > 0 && at == a.LastIndexOf('@') && a.IndexOf('.', at) > at + 1 && !a.EndsWith('.') && !a.Contains(' ');
    }
}
