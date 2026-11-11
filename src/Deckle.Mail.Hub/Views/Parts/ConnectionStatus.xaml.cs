using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

public sealed partial class ConnectionStatus : UserControl
{
    public ConnectionStatus() => InitializeComponent();

    // A mailbox that cannot connect is a caution, one severity wherever it is
    // named: its mail is still shown and nothing is lost.
    public void Show(ConnectionState state)
    {
        (string style, string key) = state switch
        {
            ConnectionState.Connected => ("SuccessDotInfoBadgeStyle", "Status_Connected"),
            ConnectionState.CannotConnect => ("CautionDotInfoBadgeStyle", "Status_CannotConnect"),
            _ => ("InformationalDotInfoBadgeStyle", "Status_NotConnected"),
        };
        Dot.Style = Application.Current.Resources[style] as Style;
        Words.Text = MailText.Get(key);
    }
}

public enum ConnectionState
{
    Connected,
    CannotConnect,
    NotConnected,
}
