using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Mail.Hub;

public sealed partial class MailSettingsPage : Page
{
    private IMailShell? _shell;
    private TriageSession? _session;

    public MailSettingsPage()
    {
        InitializeComponent();
        Header.SetTitle(MailText.Get("Nav_Settings"));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not MailPageArgs args) return;
        if (!ReferenceEquals(args.Shell.Session, _session))
        {
            if (_session is not null) _session.Changed -= OnSessionChanged;
            args.Shell.Session.Changed += OnSessionChanged;
        }
        _shell = args.Shell;
        _session = args.Shell.Session;
        Build();
    }

    private void OnSessionChanged(SessionChange change)
    {
        if ((change & SessionChange.Mailboxes) != 0) Build();
    }

    private void Build()
    {
        if (_session is null) return;
        MailboxCards.Children.Clear();
        foreach (Mailbox mailbox in _session.Mailboxes)
        {
            var status = new ConnectionStatus();
            status.Show(mailbox.Connection == MailboxConnection.Connected ? ConnectionState.Connected : ConnectionState.CannotConnect);
            var card = new SettingsCard
            {
                Header = MailNames.Mailbox(mailbox),
                Description = Describe(mailbox),
                IsClickEnabled = true,
                Content = status,
            };
            string id = mailbox.Id;
            card.Click += (_, _) => _shell?.Go(MailDestination.MailboxSettings(id));
            MailboxCards.Children.Add(card);
        }
        AnytypeStatus.Show(_session.IsAnytypeConnected ? ConnectionState.Connected : ConnectionState.NotConnected);
    }

    // The name is the header; the address and the number of aliases under it.
    private static string Describe(Mailbox mailbox)
    {
        string aliases = mailbox.Aliases.Count switch
        {
            0 => MailText.Get("Settings_NoAlias"),
            1 => MailText.Get("Settings_OneAlias"),
            int n => MailText.Format("Settings_Aliases_Format", n),
        };
        return MailNames.Mailbox(mailbox) == mailbox.Address ? aliases : MailText.Format("Join_Dot_Format", mailbox.Address, aliases);
    }

    private void OnAddMailboxClick(object sender, RoutedEventArgs e) => _shell?.Go(MailDestination.AddMailbox);
}
