using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

public sealed partial class EnvelopePicture : UserControl
{
    public EnvelopePicture()
    {
        InitializeComponent();
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }
}
