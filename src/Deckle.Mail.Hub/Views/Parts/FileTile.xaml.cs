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
        MetaText.Text = $"{file.Kind} · {MailDates.Size(file.SizeBytes)}";
        AutomationProperties.SetName(TileButton, $"{file.Name}, {MetaText.Text}");
    }
}
