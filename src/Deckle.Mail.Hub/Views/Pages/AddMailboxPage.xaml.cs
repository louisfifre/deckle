using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Mail.Hub;

// Add mailbox. The form is drawn and works in memory: Add puts the mailbox in
// the navigation and in Settings, connected and empty, and nothing connects.
// The app password is read for its length only, to enable Add, and is
// dropped with the page: it is never stored, logged or written anywhere.
public sealed partial class AddMailboxPage : Page
{
    private IMailShell? _shell;

    public AddMailboxPage()
    {
        InitializeComponent();
        Header.CrumbClicked += target => _shell?.Go(target);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not MailPageArgs args) return;
        _shell = args.Shell;
        Header.SetTrail([
            new HeaderCrumb(MailText.Get("Nav_Settings"), MailDestination.Settings),
            new HeaderCrumb(MailText.Get("AddMailbox_Title"), null),
        ]);
        UpdateForm();
    }

    // The page leaves with whatever was typed: the PasswordBox is emptied so
    // nothing of it stays in memory longer than the page.
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        PasswordField.Password = "";
        base.OnNavigatedFrom(e);
    }

    private void OnFormChanged(object sender, TextChangedEventArgs e) => UpdateForm();

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => UpdateForm();

    // A known provider fills the server and says so under the address; an
    // unknown one opens the server settings for the person (frame 1.4).
    private void UpdateForm()
    {
        string address = AddressBox.Text.Trim();
        MailServer? known = MailProviders.Find(address);
        bool looks = MailProviders.LooksLikeAddress(address);

        if (known is not null)
        {
            ProviderText.Text = MailText.Format("Join_Dot_Format", known.Provider, known.Host);
            ProviderText.Visibility = Visibility.Visible;
            ServerExpander.Description = MailText.Format("AddMailbox_ServerFound_Format", known.Provider);
            if (HostBox.FocusState == FocusState.Unfocused) HostBox.Text = known.Host;
            ServerExpander.IsExpanded = false;
        }
        else
        {
            ProviderText.Visibility = Visibility.Collapsed;
            ServerExpander.Description = MailText.Get("AddMailbox_ServerUnknown");
            if (looks) ServerExpander.IsExpanded = true;
        }

        AddButton.IsEnabled = looks
                              && PasswordField.Password.Length > 0
                              && (known is not null || HostBox.Text.Trim().Length > 0);
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (_shell is null) return;
        string address = AddressBox.Text.Trim();
        MailServer server = MailProviders.Find(address)
            ?? new MailServer("", HostBox.Text.Trim(), double.IsNaN(PortBox.Value) ? 993 : (int)PortBox.Value);
        PasswordField.Password = "";
        _shell.Session.AddMailbox(address, NameBox.Text, server);
        DeckleMailHubSource.Log.GestureApplied("add_mailbox", true);
        // Open 38 leaves open whether the person is taken to Triage or stays
        // in Settings; Settings shows the new card at once.
        _shell.Go(MailDestination.Settings);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _shell?.GoBack();
}
