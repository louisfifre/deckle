using Deckle.Catalog;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Deckle.Mail.Hub;

// The page of one mailbox in the window's Settings. A change applies at once,
// in memory: the name when its field is left or Enter is pressed, a switch
// when it is turned. The server fields are drawn and editable but go nowhere
// yet: nothing connects until the engine is wired.
public sealed partial class MailboxSettingsPage : Page
{
    private IMailShell? _shell;
    private TriageSession? _session;
    private string? _mailboxId;
    private bool _loading;

    public MailboxSettingsPage()
    {
        InitializeComponent();
        Header.CrumbClicked += target => _shell?.Go(target);
    }

    private Mailbox? Mailbox => _session?.FindMailbox(_mailboxId);

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
        bool other = _mailboxId != args.Destination.MailboxId;
        _mailboxId = args.Destination.MailboxId;
        if (other) PasswordField.Password = "";
        Build();
    }

    private void OnSessionChanged(SessionChange change)
    {
        if ((change & SessionChange.Mailboxes) != 0) Build();
    }

    private void Build()
    {
        if (Mailbox is not { } mailbox) return;
        _loading = true;
        try
        {
            string label = MailNames.Mailbox(mailbox);
            Header.SetTrail([new HeaderCrumb(MailText.Get("Nav_Settings"), MailDestination.Settings), new HeaderCrumb(label, null)]);

            // A name left empty makes the mailbox show its address.
            NameCard.Description = MailText.Format("Mailbox_NameDescription_Format", mailbox.Address);
            if (NameBox.FocusState == FocusState.Unfocused) NameBox.Text = mailbox.GivenName ?? "";
            NameBox.PlaceholderText = mailbox.Address;

            bool connected = mailbox.Connection == MailboxConnection.Connected;
            Status.Show(connected ? ConnectionState.Connected : ConnectionState.CannotConnect);
            StatusCard.Description = connected
                ? MailText.Format("Mailbox_LastChecked_Format", MailDates.Time(mailbox.ShownAsOf))
                : MailText.Format("Problem_ShownAsOf_Format", MailDates.Time(mailbox.ShownAsOf));

            PasswordStateText.Text = MailText.Get(mailbox.HasSavedPassword ? "Mailbox_PasswordSaved" : "Mailbox_PasswordNotSaved");
            ForgetPasswordButton.Visibility = mailbox.HasSavedPassword ? Visibility.Visible : Visibility.Collapsed;
            SavePasswordButton.IsEnabled = PasswordField.Password.Length > 0;

            ServerExpander.Description = string.IsNullOrEmpty(mailbox.Server.Provider)
                ? mailbox.Server.Host
                : $"{mailbox.Server.Provider} · {mailbox.Server.Host}";
            HostBox.Text = mailbox.Server.Host;
            PortBox.Value = mailbox.Server.Port;

            SortByAliasToggle.IsOn = mailbox.SortsByAlias;
            NameAliasesToggle.IsOn = mailbox.NamesAliasesFromAddress;
            AliasesExpander.IsExpanded = SortByAliasToggle.IsOn;
            // The options of the aliases apply only while the mailbox sorts by
            // alias: hidden otherwise, not greyed.

            foreach (object item in AliasesExpander.Items)
                if (item is UIElement element)
                    element.Visibility = SortByAliasToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        CommitName();
    }

    private void OnNameLostFocus(object sender, RoutedEventArgs e) => CommitName();

    private void CommitName()
    {
        if (_session is null || _mailboxId is null) return;
        _session.RenameMailbox(_mailboxId, NameBox.Text);
    }

    // The password stays in the PasswordBox; nothing reads it but its length.
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
        => SavePasswordButton.IsEnabled = PasswordField.Password.Length > 0;

    // Saving keeps only the fact that a password is saved, and empties the
    // field: a saved password is never shown again.
    private void OnSavePasswordClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || _mailboxId is null) return;
        PasswordField.Password = "";
        _session.SetPasswordSaved(_mailboxId, true);
    }

    private void OnForgetPasswordClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || _mailboxId is null) return;
        _session.SetPasswordSaved(_mailboxId, false);
    }

    private void OnSortByAliasToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _session is null || _mailboxId is null) return;
        _session.SetSortsByAlias(_mailboxId, SortByAliasToggle.IsOn);
    }

    private void OnNameAliasesToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _session is null || _mailboxId is null) return;
        _session.SetNamesAliasesFromAddress(_mailboxId, NameAliasesToggle.IsOn);
    }

    // Removal asks first, in the dialog every Deckle confirmation uses; its
    // default button is Cancel (Open 29 weighs Remove as the default, since
    // the mail stays on the server; frame 7.6 draws Cancel).
    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || Mailbox is not { } mailbox) return;
        int subjects = _session.WaitingCount(MailFilter.Mailbox(mailbox.Id));
        string body = subjects switch
        {
            0 => MailText.Get("Remove_Body"),
            1 => $"{MailText.Get("Remove_Body")} {MailText.Get("Remove_OneSubject")}",
            int n => $"{MailText.Get("Remove_Body")} {MailText.Format("Remove_Subjects_Format", n)}",
        };
        bool confirmed = await ConfirmationService.RequestAsync(XamlRoot, new ConfirmationRequest(
            MailText.Format("Remove_Title_Format", MailNames.Mailbox(mailbox)),
            body,
            MailText.Get("Remove_Primary"),
            IsDestructive: true));
        if (!confirmed) return;

        _session.RemoveMailbox(mailbox.Id);
        _shell?.Go(MailDestination.Settings);
    }
}
