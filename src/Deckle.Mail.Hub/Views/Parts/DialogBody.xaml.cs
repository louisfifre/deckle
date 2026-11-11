using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

public sealed partial class DialogBody : UserControl
{
    public DialogBody(string main, string? detail = null, string? detailLanguage = null)
    {
        InitializeComponent();
        MainText.Text = main;
        if (!string.IsNullOrEmpty(detail))
        {
            DetailText.Text = detail;
            if (detailLanguage is not null) DetailText.Language = detailLanguage;
            DetailText.Visibility = Visibility.Visible;
        }
    }
}
