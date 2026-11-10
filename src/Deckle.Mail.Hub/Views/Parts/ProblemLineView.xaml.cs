using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

// A line of the problems list. A mailbox that cannot connect is a caution,
// the same severity wherever it is named, since its mail is still shown and
// nothing is lost (spec, Settings); Anytype not connected is information.
public sealed partial class ProblemLineView : UserControl
{
    private readonly Action _command;
    private readonly Action? _dismiss;

    public ProblemLineView(MailProblem problem, TriageSession session, Action command, Action? dismiss)
    {
        InitializeComponent();
        _command = command;
        _dismiss = dismiss;

        if (problem.Kind == MailProblemKind.MailboxCannotConnect && session.FindMailbox(problem.MailboxId) is { } mailbox)
        {
            Badge.Style = BadgeStyle("CautionIconInfoBadgeStyle");
            TitleText.Text = MailText.Format("Problem_CannotConnect_Format", MailNames.Mailbox(mailbox));
            string shownAsOf = MailText.Format("Problem_ShownAsOf_Format", MailDates.Time(mailbox.ShownAsOf));
            MessageText.Text = problem.WaitingGestures switch
            {
                0 => shownAsOf,
                1 => $"{shownAsOf} {MailText.Get("Problem_OneChangeWaits")}",
                int n => $"{shownAsOf} {MailText.Format("Problem_ChangesWait_Format", n)}",
            };
        }
        else
        {
            Badge.Style = BadgeStyle("InformationalIconInfoBadgeStyle");
            TitleText.Text = MailText.Get("Problem_AnytypeNotConnected");
            MessageText.Text = MailText.Get("Problem_AnytypeNotConnected_Message");
        }

        CommandButton.Content = MailText.Get("Problem_OpenSettings");
        if (dismiss is not null)
        {
            DismissButton.Visibility = Visibility.Visible;
            string dismissName = MailText.Get("Problem_DismissAnytype");
            ToolTipService.SetToolTip(DismissButton, dismissName);
            AutomationProperties.SetName(DismissButton, dismissName);
        }
    }

    private static Style? BadgeStyle(string key)
        => Application.Current.Resources.TryGetValue(key, out object? style) ? style as Style : null;

    private void OnCommandClick(object sender, RoutedEventArgs e) => _command();

    private void OnDismissClick(object sender, RoutedEventArgs e) => _dismiss?.Invoke();
}
