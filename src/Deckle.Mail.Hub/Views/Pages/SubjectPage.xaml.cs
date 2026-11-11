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
    // Under this width of the window the list of mails goes under the text
    // (spec, A narrow window): the 860 the page's text column of sixty
    // characters and its 288 px list need side by side, plus the open
    // navigation at its widest (296, MailWindow.Rail.cs).
    //
    // The window's width decides, not the page's: the navigation folds to its
    // rail in a narrower window and hands the page its room back, so a page
    // measured on its own width put the list beside the text again in a
    // window narrower than the one that had just stacked it.
    private const double StackBelowWindow = 860 + NavigationAtItsWidest;
    private const double NavigationAtItsWidest = 296;

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
        ComponentMotion.Still(MailsList);
        ComponentMotion.Still(UnverifiedFlyout);
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
        string mark = MailText.Get("Mark_SenderNotVerified");
        UnverifiedMarkText.Text = mark;
        UnverifiedTitle.Text = mark;
        UnverifiedMessage.Text = MailText.Get("Subject_Unverified_Message");
        AutomationProperties.SetName(UnverifiedMark, mark);
        TitleText.Text = MailText.Typeset(_subject.Sentence);
        TitleText.Language = _subject.Language;
        AutomationProperties.SetName(TrashButton, MailText.Format("Row_TrashName_Format", _subject.Sentence));

        MailsList.Items.Clear();
        foreach (SubjectMail mail in _subject.Mails)
            MailsList.Items.Add(new MailListItemView(mail, _subject.Language, _session.Today,
                showAddress: _subject.Mark == SubjectMark.SenderNotVerified));
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
        ApplyLayout();
    }

    private void ShowMail(SubjectMail mail)
    {
        if (_subject is null || _session is null) return;
        _showingLatest = ReferenceEquals(mail, _subject.Latest);

        string when = MailText.DayAndTime(mail.Received, _session.Today);
        bool unverified = _subject.Mark == SubjectMark.SenderNotVerified && !mail.IsFromYou;
        if (unverified)
        {
            // The address alone: the name a sender gives itself is what an
            // unverified sender can fake.
            FromText.Text = mail.SenderAddress;
            WhenText.Text = MailText.Format("Subject_AfterMark_Format", when);
        }
        else
        {
            string sender = mail.IsFromYou ? MailText.Get("Subject_You") : mail.SenderName;
            FromText.Text = MailText.Format("Join_Dot3_Format", sender, mail.SenderAddress, when);
        }
        UnverifiedMark.Visibility = unverified ? Visibility.Visible : Visibility.Collapsed;
        WhenText.Visibility = unverified ? Visibility.Visible : Visibility.Collapsed;
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
        // The page's size changes with the window and with the navigation;
        // only the window's width is read. Before the page is in a window it
        // has its own width to go by.
        double window = XamlRoot?.Size.Width ?? e.NewSize.Width + NavigationAtItsWidest;
        _stacked = window < StackBelowWindow;
        ApplyLayout();
    }

    private bool _stacked;

    // One width of the window decides both: under it the list of mails goes under the text,
    // and the two commands go under the grey line, at the title's left edge.
    private void ApplyLayout()
    {
        Grid.SetColumn(ListPanel, _stacked ? 0 : 1);
        Grid.SetRow(ListPanel, _stacked ? 1 : 0);
        ListColumn.Width = _stacked ? new GridLength(0) : new GridLength(288);
        MailGrid.ColumnSpacing = _stacked ? 0 : 56;
        // The grey line is one line in a wide window. A horizontal panel
        // gives its text no width to wrap in, so a narrow window cut the
        // address mid-word; stacked, the line wraps and its mark and link
        // stand under it.
        FromPanel.Orientation = _stacked ? Orientation.Vertical : Orientation.Horizontal;

        // No gesture left on this subject (treated before, or set aside): the
        // block leaves and the title takes its width.
        bool commands = !Task.IsEmpty || TrashButton.Visibility == Visibility.Visible;
        Commands.Visibility = commands ? Visibility.Visible : Visibility.Collapsed;
        bool beside = commands && !_stacked;

        Grid.SetColumnSpan(TitleText, beside ? 1 : 2);
        Grid.SetColumn(Commands, beside ? 1 : 0);
        Grid.SetRow(Commands, beside ? 0 : 2);
        Commands.HorizontalAlignment = beside ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        // Under the grey line the button's own padding (11 px) is taken back,
        // so its words start where the title starts.
        Commands.Margin = beside ? new Thickness(0, 2, 0, 0) : new Thickness(-11, 12, 0, 0);
    }
}
