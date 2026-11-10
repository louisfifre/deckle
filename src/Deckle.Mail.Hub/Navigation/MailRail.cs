namespace Deckle.Mail.Hub;

// The navigation as data: what it lists, in order, with the number each
// mailbox item wears. The window turns it into NavigationViewItems; the rules live here so
// they are tested without a window.
//
// Triage, then Activity (absent while there is no activity at all), then the
// mailboxes, then Settings at the foot. A mailbox that sorts by alias and has
// aliases is a group: Inbox first, its aliases after. A folded group wears the
// number of its whole mailbox; unfolded, it wears none and its children wear
// theirs. Inbox wears the total of the mailbox (Open 8 leans on the form the
// frames draw). An item where nothing waits shows no number.
public static class MailRail
{
    public static IReadOnlyList<RailEntry> Build(TriageSession session, Func<string, bool> isExpanded)
    {
        var entries = new List<RailEntry>
        {
            // Triage wears no number: it is the page where everything waits,
            // and the frames draw it bare.
            new(RailEntryKind.Triage, MailDestination.Triage, null, null),
        };
        if (session.HasActivity)
            entries.Add(new(RailEntryKind.Activity, MailDestination.Activity(), null, null));

        foreach (Mailbox mailbox in session.Mailboxes)
        {
            string label = MailNames.Mailbox(mailbox);
            int total = session.WaitingCount(MailFilter.Mailbox(mailbox.Id));
            bool fails = mailbox.Connection == MailboxConnection.CannotConnect;

            if (!mailbox.IsGroup)
            {
                entries.Add(new(RailEntryKind.Mailbox, MailDestination.Mailbox(mailbox.Id), label, Count(total))
                {
                    MailboxId = mailbox.Id,
                    CannotConnect = fails,
                });
                continue;
            }

            bool expanded = isExpanded(mailbox.Id);
            var children = new List<RailEntry>
            {
                new(RailEntryKind.Inbox, MailDestination.Mailbox(mailbox.Id), null, Count(total)) { MailboxId = mailbox.Id },
            };
            foreach (MailAlias alias in mailbox.Aliases)
            {
                children.Add(new(RailEntryKind.Alias, MailDestination.Alias(mailbox.Id, alias.Id),
                    MailNames.Alias(alias, mailbox.NamesAliasesFromAddress),
                    Count(session.WaitingCount(MailFilter.Alias(mailbox.Id, alias.Id))))
                {
                    MailboxId = mailbox.Id,
                    AliasId = alias.Id,
                });
            }
            entries.Add(new(RailEntryKind.Group, null, label, expanded ? null : Count(total))
            {
                MailboxId = mailbox.Id,
                CannotConnect = fails,
                Children = children,
            });
        }

        entries.Add(new(RailEntryKind.Settings, MailDestination.Settings, null, null));
        return entries;
    }

    private static int? Count(int n) => n > 0 ? n : null;
}

// Label is null where the window supplies its own word (Triage, Activity,
// Inbox, Settings); Destination is null for a group header, which opens no
// page.
public sealed record RailEntry(RailEntryKind Kind, MailDestination? Destination, string? Label, int? Count)
{
    public string? MailboxId { get; init; }
    public string? AliasId { get; init; }
    public bool CannotConnect { get; init; }
    public IReadOnlyList<RailEntry> Children { get; init; } = [];
}

public enum RailEntryKind
{
    Triage,
    Activity,
    Group,
    Mailbox,
    Inbox,
    Alias,
    Settings,
}
