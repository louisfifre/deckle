namespace Deckle.Mail.Hub;

// A page of the Mail window and what it shows. Records compare by value, so
// the history can tell "the same page again" from a new one.
public sealed record MailDestination(
    MailPage Page,
    string? MailboxId = null,
    string? AliasId = null,
    string? SubjectId = null,
    ActivityPart Part = ActivityPart.ByYou)
{
    public static MailDestination Triage { get; } = new(MailPage.Triage);

    public static MailDestination Settings { get; } = new(MailPage.Settings);

    public static MailDestination AddMailbox { get; } = new(MailPage.AddMailbox);

    public static MailDestination Activity(ActivityPart part = ActivityPart.ByYou) => new(MailPage.Activity, Part: part);

    // Inbox under a group, or the single item of a mailbox without aliases.
    public static MailDestination Mailbox(string mailboxId) => new(MailPage.Mailbox, mailboxId);

    public static MailDestination Alias(string mailboxId, string aliasId) => new(MailPage.Alias, mailboxId, aliasId);

    public static MailDestination Subject(string subjectId) => new(MailPage.Subject, SubjectId: subjectId);

    public static MailDestination MailboxSettings(string mailboxId) => new(MailPage.MailboxSettings, mailboxId);

    // The Triage a list page shows; null for a page that is not a list.
    public MailFilter? Filter => Page switch
    {
        MailPage.Triage => MailFilter.All,
        MailPage.Mailbox when MailboxId is not null => MailFilter.Mailbox(MailboxId),
        MailPage.Alias when MailboxId is not null && AliasId is not null => MailFilter.Alias(MailboxId, AliasId),
        _ => null,
    };
}

public enum MailPage
{
    Triage,
    Mailbox,
    Alias,
    Subject,
    Activity,
    Settings,
    MailboxSettings,
    AddMailbox,
}
