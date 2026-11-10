using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

public sealed partial class MailListItemView : UserControl
{
    public MailListItemView(SubjectMail mail, string language, DateOnly today)
    {
        InitializeComponent();
        Mail = mail;
        string sender = mail.IsFromYou ? MailText.Get("Subject_You") : mail.SenderName;
        MetaRun.Text = $"{MailDates.Day(DateOnly.FromDateTime(mail.Received.DateTime), today)} · {sender}";
        if (mail.Attachments.Count > 0) ClipRun.Text = "  " + Deckle.Catalog.Glyphs.Mail.Attachment;
        SummaryText.Text = mail.Summary;
        SummaryText.Language = language;
        AutomationProperties.SetName(this, $"{MetaRun.Text}, {mail.Summary}");
    }

    public SubjectMail Mail { get; }
}
