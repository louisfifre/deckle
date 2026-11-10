using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

// The notice of the first trash (frame 1.9): where the mails go, what the
// provider does with them and for how long Undo can bring them back. The
// gesture happens only on its primary button; Cancel, the default, does
// nothing.
internal static class TrashNotice
{
    public static async Task<bool> AskAsync(XamlRoot root, TriageSession session, MailSubject subject)
    {
        Mailbox? mailbox = session.FindMailbox(subject.MailboxId);
        string where = mailbox is null ? ""
            : MailNames.Mailbox(mailbox) == mailbox.Address ? mailbox.Address
            : MailText.Format("TrashNotice_Where_Format", MailNames.Mailbox(mailbox), mailbox.Address);

        var dialog = new ContentDialog
        {
            Title = MailText.Get("TrashNotice_Title"),
            Content = new DialogBody(
                MailText.Format("TrashNotice_Body_Format", where, mailbox?.Server.Provider ?? "", mailbox?.Server.TrashDays ?? 30),
                subject.Sentence, subject.Language),
            PrimaryButtonText = MailText.Get("TrashNotice_Primary"),
            CloseButtonText = MailText.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
