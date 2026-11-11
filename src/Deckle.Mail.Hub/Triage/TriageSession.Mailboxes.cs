namespace Deckle.Mail.Hub;

// What the Settings pages of the window change, in memory only. Nothing
// connects: a mailbox added here is shown, not reached. An app password is
// never part of it; the session keeps only whether one is saved.
public sealed partial class TriageSession
{
    public void RenameMailbox(string mailboxId, string? name)
        => Replace(mailboxId, m => m with { GivenName = string.IsNullOrWhiteSpace(name) ? null : name.Trim() });

    public void RenameAlias(string mailboxId, string aliasId, string? name)
        => Replace(mailboxId, m => m with
        {
            Aliases = [.. m.Aliases.Select(a => string.Equals(a.Id, aliasId, StringComparison.Ordinal)
                ? a with { GivenName = string.IsNullOrWhiteSpace(name) ? null : name.Trim() }
                : a)],
        });

    public void SetSortsByAlias(string mailboxId, bool on) => Replace(mailboxId, m => m with { SortsByAlias = on });

    public void SetNamesAliasesFromAddress(string mailboxId, bool on)
        => Replace(mailboxId, m => m with { NamesAliasesFromAddress = on });

    // Save password and Forget password. The password itself stays in the
    // control that took it: here is only the fact that one is saved.
    public void SetPasswordSaved(string mailboxId, bool saved)
        => Replace(mailboxId, m => m with { HasSavedPassword = saved });

    public void RemoveMailbox(string mailboxId)
    {
        if (_mailboxes.RemoveAll(m => string.Equals(m.Id, mailboxId, StringComparison.Ordinal)) == 0) return;
        _rows.RemoveAll(r => string.Equals(r.Subject.MailboxId, mailboxId, StringComparison.Ordinal));
        _infos.RemoveAll(i => string.Equals(i.MailboxId, mailboxId, StringComparison.Ordinal));
        _activity.RemoveAll(a => string.Equals(a.MailboxId, mailboxId, StringComparison.Ordinal));
        foreach (string id in _subjects.Values.Where(s => string.Equals(s.MailboxId, mailboxId, StringComparison.Ordinal))
                     .Select(s => s.Id).ToList())
            _subjects.Remove(id);
        Raise(SessionChange.Mailboxes | SessionChange.Rows | SessionChange.InfoLines
              | SessionChange.Activity | SessionChange.Problems);
    }

    // The mailbox is shown at once, connected and empty: the sample has no
    // mail to sort for it.
    public Mailbox AddMailbox(string address, string? name, MailServer server)
    {
        var mailbox = new Mailbox(
            $"mb-{Guid.NewGuid():N}", address.Trim(), string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            [], MailboxConnection.Connected, HasSavedPassword: true, server,
            SortsByAlias: true, NamesAliasesFromAddress: true, Now);
        _mailboxes.Add(mailbox);
        Raise(SessionChange.Mailboxes | SessionChange.Problems);
        return mailbox;
    }

    private void Replace(string mailboxId, Func<Mailbox, Mailbox> change)
    {
        int index = _mailboxes.FindIndex(m => string.Equals(m.Id, mailboxId, StringComparison.Ordinal));
        if (index < 0) return;
        Mailbox next = change(_mailboxes[index]);
        if (next == _mailboxes[index]) return;
        _mailboxes[index] = next;
        Raise(SessionChange.Mailboxes);
    }
}
