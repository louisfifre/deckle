using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

public sealed partial class FramedLabel : UserControl
{
    public FramedLabel() => InitializeComponent();

    public string Text
    {
        get => LabelText.Text;
        set => LabelText.Text = value;
    }
}
