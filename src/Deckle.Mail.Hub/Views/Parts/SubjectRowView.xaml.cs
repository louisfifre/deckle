using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

// A row of Triage, bound to its TriageRow for the life of the page. Its
// gestures go to the session; Refresh redraws it from the row, in place, so
// the focus stays where the hand is.
public sealed partial class SubjectRowView : UserControl
{
    private readonly TriageSession _session;

    public SubjectRowView(TriageSession session, TriageRow row)
    {
        InitializeComponent();
        _session = session;
        Row = row;
        Task.DayChosen += OnDayChosen;
        Task.UndoRequested += OnUndo;
        Task.AcceptsClick = () => _session.AcceptsClick(Row.Id);

        MailSubject subject = row.Subject;
        SentenceRun.Text = subject.Sentence;
        SentenceText.Language = subject.Language;
        if (subject.HasAttachment)
        {
            // A non-breaking space ties the clip to the last word.
            ClipRun.Text = "  " + Deckle.Catalog.Glyphs.Mail.Attachment;
        }
        if (subject.Mark != SubjectMark.None)
        {
            MarkLabel.Text = MailText.Mark(subject.Mark);
            MarkLabel.Visibility = Visibility.Visible;
        }
        AutomationProperties.SetName(TrashButton, MailText.Format("Row_TrashName_Format", subject.Sentence));
        AutomationProperties.SetName(RemoveButton, MailText.Format("Row_RemoveName_Format", subject.Sentence));
        Refresh();
    }

    public TriageRow Row { get; }

    // The task column, for the self-check and the keyboard: focus goes to
    // Undo after a gesture.
    public TaskColumn TaskColumn => Task;

    public void Refresh()
    {
        TriageRow row = Row;
        MailSubject subject = row.Subject;
        bool treated = row.IsTreated;
        VisualStateManager.GoToState(this, treated ? "Treated" : "Waiting", false);

        string name = subject.HasAttachment
            ? MailText.Format("Row_NameWithAttachment_Format", subject.Sentence)
            : subject.Sentence;
        AutomationProperties.SetName(this, name);

        if (row.Result is { } result)
        {
            Mailbox? mailbox = _session.FindMailbox(subject.MailboxId);
            Task.ShowResult(MailText.Result(result, _session.Today), _session.WaitsForMailbox(row),
                mailbox is null ? null : MailNames.Mailbox(mailbox));
            Task.SetUndoName(MailText.Format("Row_UndoName_Format", subject.Sentence));
        }
        else if (_session.IsAnytypeConnected && subject.CanBecomeTask)
        {
            Task.ShowAddTask(TaskDays.Propose(subject.Deadline, _session.Today), _session.Today, subject.Sentence);
        }
        else
        {
            Task.ShowEmpty();
        }

        TrashButton.Visibility = treated ? Visibility.Collapsed : Visibility.Visible;
        RemoveButton.Visibility = treated ? Visibility.Visible : Visibility.Collapsed;
    }

    // After a gesture the focus goes on Undo of the same row (Open 31 leaves
    // 31FG and 32FN open; frame 8.3 draws this one).
    private void FocusUndo()
    {
        if (Task.UndoTarget.FocusState == FocusState.Unfocused && XamlRoot is not null)
            Task.UndoTarget.Focus(FocusState.Programmatic);
    }

    private void OnDayChosen(DateOnly? day)
    {
        bool applied = _session.AddTask(Row.Id, day);
        DeckleMailHubSource.Log.GestureApplied("add_task", applied);
        if (applied) FocusUndo();
    }

    private async void OnTrashClick(object sender, RoutedEventArgs e)
    {
        if (!_session.AcceptsClick(Row.Id)) return;
        if (!_session.IsTrashNoticeAccepted)
        {
            if (!await TrashNotice.AskAsync(XamlRoot, _session, Row.Subject)) return;
            _session.AcceptTrashNotice();
        }
        bool applied = _session.MoveToTrash(Row.Id);
        DeckleMailHubSource.Log.GestureApplied("move_to_trash", applied);
        if (applied) FocusUndo();
    }

    private void OnUndo()
    {
        if (!_session.AcceptsClick(Row.Id)) return;
        bool applied = _session.Undo(Row.Id);
        DeckleMailHubSource.Log.GestureApplied("undo", applied);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (!_session.AcceptsClick(Row.Id)) return;
        _session.RemoveTreatedRow(Row.Id);
    }
}
