using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Mail.Hub;

// ── A subject opened ──────────────────────────────────────────────────────────
//
// Reached from a row, an Activity line or the search; left by the back button
// of the title bar, Alt+Left or the mouse back button. Shows the latest mail
// whole and, beside it, every mail of the subject; clicking another shows
// that one in its place and the list stays.
//
// The two gestures of the row stand beside the title (Open 4 leans on 18K;
// the « Reply in webmail » link 18K adds is left out: the sample has no
// webmail to open). After a gesture, Undo and the result stand in the task
// column and the end column is empty: the page has no line to remove.
public sealed partial class SubjectPage : Page
{
    // Under this width the list of mails goes under the text (spec, A narrow
    // window): the text column's sixty characters and the 288 px list no
    // longer fit side by side.
    private const double StackBelow = 860;

    private IMailShell? _shell;
    private TriageSession? _session;
    private MailSubject? _subject;
    private bool _showingLatest = true;

    public SubjectPage()
    {
        InitializeComponent();
        Task.DayChosen += OnDayChosen;
        Task.UndoRequested += OnUndo;
        Task.AcceptsClick = () => _subject is null || _session is null || _session.AcceptsClick(_subject.Id);
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
        _subject = _session.FindSubject(args.Destination.SubjectId);
        Scroller.ChangeView(null, 0, null, disableAnimation: true);
        Build();
    }

    private void OnSessionChanged(SessionChange change)
    {
        if ((change & SessionChange.Rows) != 0) RefreshCommands();
    }

    private void Build()
    {
        if (_subject is null || _session is null) return;
        TitleText.Text = _subject.Sentence;
        TitleText.Language = _subject.Language;
        AutomationProperties.SetName(TrashButton, MailText.Format("Row_TrashName_Format", _subject.Sentence));

        MailsList.Items.Clear();
        foreach (SubjectMail mail in _subject.Mails)
            MailsList.Items.Add(new MailListItemView(mail, _subject.Language, _session.Today));
        CountText.Text = _subject.Mails.Count == 1
            ? MailText.Get("Subject_OneMail")
            : MailText.Format("Subject_Mails_Format", _subject.Mails.Count);
        MailsList.SelectedIndex = 0;
        ShowMail(_subject.Latest);
        RefreshCommands();
    }

    // The two columns follow the subject's row: Add task and the trash while
    // it waits; Undo and the result once treated; nothing where the subject
    // is not in Triage at all (treated before, or set aside).
    private void RefreshCommands()
    {
        if (_subject is null || _session is null) return;
        TriageRow? row = _session.FindRow(_subject.Id);

        if (row?.Result is { } result)
        {
            Mailbox? mailbox = _session.FindMailbox(_subject.MailboxId);
            Task.ShowResult(MailText.Result(result, _session.Today), _session.WaitsForMailbox(row),
                mailbox is null ? null : MailNames.Mailbox(mailbox));
            Task.SetUndoName(MailText.Format("Row_UndoName_Format", _subject.Sentence));
            TrashButton.Visibility = Visibility.Collapsed;
        }
        else if (row is not null)
        {
            if (_session.IsAnytypeConnected && _subject.CanBecomeTask)
                Task.ShowAddTask(TaskDays.Propose(_subject.Deadline, _session.Today), _session.Today, _subject.Sentence);
            else
                Task.ShowEmpty();
            TrashButton.Visibility = Visibility.Visible;
        }
        else
        {
            Task.ShowEmpty();
            TrashButton.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowMail(SubjectMail mail)
    {
        if (_subject is null || _session is null) return;
        _showingLatest = ReferenceEquals(mail, _subject.Latest);

        string sender = mail.IsFromYou ? MailText.Get("Subject_You") : mail.SenderName;
        FromText.Text = $"{sender} · {mail.SenderAddress} · {MailDates.DayAndTime(mail.Received, _session.Today)}";
        EarlierText.Visibility = _showingLatest ? Visibility.Collapsed : Visibility.Visible;
        LatestLink.Visibility = _showingLatest ? Visibility.Collapsed : Visibility.Visible;

        BodyPanel.Children.Clear();
        if (mail.Body.Count == 0)
        {
            // A mail with no text says so where the text would be.
            BodyPanel.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Informational,
                Message = MailText.Get("Subject_NoText"),
            });
        }
        foreach (string paragraph in mail.Body)
        {
            BodyPanel.Children.Add(new TextBlock
            {
                Text = paragraph,
                Language = _subject.Language,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
        }

        // The files really attached to that mail, each a tile with its name,
        // kind and size. An image of the body is never listed here.
        FilesPanel.Children.Clear();
        foreach (MailAttachment file in mail.Attachments)
            FilesPanel.Children.Add(new FileTile(file));
        FilesPanel.Visibility = mail.Attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMailSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MailsList.SelectedItem is MailListItemView item) ShowMail(item.Mail);
    }

    private void OnShowLatestClick(object sender, RoutedEventArgs e) => MailsList.SelectedIndex = 0;

    // Shows a mail of the list as a click on it would; for the self-check.
    public void ShowMailAt(int index)
    {
        if (index >= 0 && index < MailsList.Items.Count) MailsList.SelectedIndex = index;
    }

    private void OnDayChosen(DateOnly? day)
    {
        if (_subject is null || _session is null) return;
        bool applied = _session.AddTask(_subject.Id, day);
        DeckleMailHubSource.Log.GestureApplied("add_task", applied);
    }

    private async void OnTrashClick(object sender, RoutedEventArgs e)
    {
        if (_subject is null || _session is null || !_session.AcceptsClick(_subject.Id)) return;
        if (!_session.IsTrashNoticeAccepted)
        {
            if (!await TrashNotice.AskAsync(XamlRoot, _session, _subject)) return;
            _session.AcceptTrashNotice();
        }
        bool applied = _session.MoveToTrash(_subject.Id);
        DeckleMailHubSource.Log.GestureApplied("move_to_trash", applied);
    }

    private void OnUndo()
    {
        if (_subject is null || _session is null || !_session.AcceptsClick(_subject.Id)) return;
        bool applied = _session.Undo(_subject.Id);
        DeckleMailHubSource.Log.GestureApplied("undo", applied);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool stacked = e.NewSize.Width < StackBelow;
        Grid.SetColumn(ListPanel, stacked ? 0 : 1);
        Grid.SetRow(ListPanel, stacked ? 1 : 0);
        ListColumn.Width = stacked ? new GridLength(0) : new GridLength(288);
        MailGrid.ColumnSpacing = stacked ? 0 : 56;
    }
}
