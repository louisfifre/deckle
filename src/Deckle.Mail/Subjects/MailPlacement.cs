namespace Deckle.Mail;

/// <summary>
/// The rules that turn the model's judgment into the place a mail lands in.
/// Every rule here moves a mail toward Triage, never away from it: a wrong
/// set-aside is silent and the provider deletes it from the trash after its
/// delay, while a wrong row costs one click.
/// </summary>
internal static class MailPlacement
{
    public static (MailPlace Place, SetAsideReason? Reason) Place(
        Mail mail,
        MailJudgment judgment,
        IReadOnlySet<SortingCorrection> corrections)
    {
        // An unverified sender waits in Triage with its framed label, whatever
        // the model thought of the text: the person has to see it to trash it.
        if (!mail.SenderVerified)
        {
            return (MailPlace.Triage, null);
        }

        // Never set aside unread.
        if (judgment.Unreadable is not null)
        {
            return (MailPlace.Triage, null);
        }

        if (judgment.Place != MailPlace.SetAside)
        {
            return (judgment.Place, null);
        }

        var reason = judgment.SetAsideReason!.Value;
        if (judgment.Doubtful || corrections.Contains(SortingCorrection.For(mail.From.Address, reason)))
        {
            return (MailPlace.Triage, null);
        }

        return (MailPlace.SetAside, reason);
    }
}
