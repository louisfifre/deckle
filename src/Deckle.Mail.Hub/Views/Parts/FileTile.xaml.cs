using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

// The sample carries no file on disk, so the tile opens nothing yet: the
// engine hands the file to Windows when it is wired.
public sealed partial class FileTile : UserControl
{
    public FileTile(MailAttachment file)
    {
        InitializeComponent();
        NameText.Text = file.Name;
        MetaText.Text = MailText.Format("Join_Dot_Format", file.Kind, MailText.Size(file.SizeBytes));
        AutomationProperties.SetName(TileButton, MailText.Format("Join_Comma_Format", file.Name, MetaText.Text));
    }
}
