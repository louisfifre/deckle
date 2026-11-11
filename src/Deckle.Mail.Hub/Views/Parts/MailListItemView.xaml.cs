using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

public sealed partial class MailListItemView : UserControl
{
    // showAddress: a sender not verified is named by its address, never by
    // the name it gave itself.
    public MailListItemView(SubjectMail mail, string language, DateOnly today, bool showAddress = false)
    {
        InitializeComponent();
        Mail = mail;
        string sender = mail.IsFromYou ? MailText.Get("Subject_You")
            : showAddress ? mail.SenderAddress
            : mail.SenderName;
        MetaRun.Text = MailText.Format("Join_Dot_Format", MailDates.Day(DateOnly.FromDateTime(mail.Received.DateTime), today), sender);
        if (mail.Attachments.Count > 0) ClipRun.Text = "  " + Deckle.Catalog.Glyphs.Mail.Attachment;
        SummaryText.Text = MailText.Typeset(mail.Summary);
        SummaryText.Language = language;
        AutomationProperties.SetName(this, MailText.Format("Join_Comma_Format", MetaRun.Text, mail.Summary));
    }

    public SubjectMail Mail { get; }
}
