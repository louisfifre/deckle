using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Mail.Hub;

// The task column's states, set by its row, its page or its Activity line.
// It owns the menu of days and the calendar of Pick a day; what a choice does
// is its host's (DayChosen, UndoRequested).
public sealed partial class TaskColumn : UserControl
{
    private DateOnly _today;
    private DateOnly? _proposed;
    private Flyout? _calendarFlyout;

    public TaskColumn() => InitializeComponent();

    // A day of the menu was chosen: null for No day yet.
    public event Action<DateOnly?>? DayChosen;

    public event Action? UndoRequested;

    // Asked before the menu opens: false in the instant after a gesture on
    // the same row (TriageSession.AcceptsClick), so the second click of a
    // double click on Undo, landing on the Add task that Undo brings back,
    // opens nothing. Null accepts every click.
    public Func<bool>? AcceptsClick { get; set; }

    public Button UndoTarget => UndoButton;

    public Button AddTaskTarget => AddTaskButton;

    // True while the column holds nothing: neither Add task nor a result.
    public bool IsEmpty
        => AddTaskButton.Visibility == Visibility.Collapsed && DonePanel.Visibility == Visibility.Collapsed;

    // Waiting with Add task. accessibleSentence names what the task would be
    // about, so a screen reader does not hear "Add task" four times.
    public void ShowAddTask(DateOnly? proposed, DateOnly today, string accessibleSentence)
    {
        _today = today;
        _proposed = proposed;
        AddTaskText.Text = MailText.AddTask(proposed, today);
        AutomationProperties.SetName(AddTaskButton, MailText.Format("Task_AddName_Format", AddTaskText.Text, accessibleSentence));
        _menu = BuildMenu();
        AddTaskButton.Visibility = Visibility.Visible;
        DonePanel.Visibility = Visibility.Collapsed;
    }

    // Without Anytype, or for a sender not verified: the column stays, empty,
    // so the trash stands where every other trash stands.
    public void ShowEmpty()
    {
        AddTaskButton.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Collapsed;
    }

    // After a gesture: Undo, then what happened in words, then the caution
    // dot while the gesture waits for its mailbox.
    public void ShowResult(string result, bool waitsForMailbox, string? waitingFor)
    {
        ShowDone(canUndo: true);
        ResultText.Text = result;
        ResultText.Visibility = Visibility.Visible;
        ReasonLabel.Visibility = Visibility.Collapsed;
        PendingDot.Visibility = waitsForMailbox ? Visibility.Visible : Visibility.Collapsed;
        if (waitsForMailbox && waitingFor is not null)
        {
            string name = MailText.Format("Task_WaitsFor_Format", waitingFor);
            ToolTipService.SetToolTip(PendingDot, name);
            AutomationProperties.SetName(PendingDot, name);
        }
    }

    // A line of the sorting's part: Undo, then the framed word of its reason.
    public void ShowReason(string reason)
    {
        ShowDone(canUndo: true);
        ResultText.Visibility = Visibility.Collapsed;
        ReasonLabel.Text = reason;
        ReasonLabel.Visibility = Visibility.Visible;
        PendingDot.Visibility = Visibility.Collapsed;
    }

    // A line that was undone: no Undo, its place kept, and the words.
    public void ShowUndone(string words)
    {
        ShowDone(canUndo: false);
        ResultText.Text = words;
        ResultText.Visibility = Visibility.Visible;
        ReasonLabel.Visibility = Visibility.Collapsed;
        PendingDot.Visibility = Visibility.Collapsed;
    }

    public void SetUndoName(string accessibleName) => AutomationProperties.SetName(UndoButton, accessibleName);

    // For the self-check: the menu as a click opens it. constrainToWindow
    // draws it inside the window, where a window capture sees it.
    public void OpenMenu(bool constrainToWindow = false)
    {
        if (_menu is null) return;
        // Set only when asked: WinUI refuses the change once the menu has
        // opened, so a person's second click must not touch it.
        if (constrainToWindow && !_menu.ShouldConstrainToRootBounds)
            MailWindow.TryConstrain(() => _menu.ShouldConstrainToRootBounds = true);
        _menu.ShowAt(AddTaskButton);
    }

    // The menu is opened here rather than through Button.Flyout, which opens
    // on every click and cannot be refused one.
    private MenuFlyout? _menu;

    private void OnAddTaskClick(object sender, RoutedEventArgs e)
    {
        if (AcceptsClick?.Invoke() == false) return;
        OpenMenu();
    }

    private void ShowDone(bool canUndo)
    {
        AddTaskButton.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Visible;
        UndoButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        UndoPlaceholder.Visibility = canUndo ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => UndoRequested?.Invoke();

    // ── The menu of days ─────────────────────────────────────────────────────
    //
    // The day each line stands for is written at the line's end, in the slot
    // Windows keeps for a shortcut (KeyboardAcceleratorTextOverride), as Outlook
    // writes the hour of its snooze choices: native, and right-aligned in the
    // menu's own grey.
    private MenuFlyout BuildMenu()
    {
        var menu = ComponentMotion.Still(new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
        MenuFlyoutItem? selected = null;

        foreach (TaskDayChoice choice in TaskDays.Menu(_proposed, _today))
        {
            if (choice.Kind == TaskDayChoiceKind.NoDay)
                menu.Items.Add(new MenuFlyoutSeparator());

            var item = new MenuFlyoutItem
            {
                Text = choice.Kind switch
                {
                    TaskDayChoiceKind.Proposed => MailText.NoBreak(MailDates.Day(choice.Day!.Value, _today)),
                    TaskDayChoiceKind.Today => MailText.Get("Menu_Today"),
                    TaskDayChoiceKind.Tomorrow => MailText.Get("Menu_Tomorrow"),
                    TaskDayChoiceKind.PickADay => MailText.Get("Menu_PickADay"),
                    _ => MailText.Get("Menu_NoDay"),
                },
                KeyboardAcceleratorTextOverride = choice.Kind switch
                {
                    TaskDayChoiceKind.Proposed => MailText.Get("Menu_FromTheMail"),
                    TaskDayChoiceKind.Today or TaskDayChoiceKind.Tomorrow => MailDates.Day(choice.Day!.Value, _today),
                    _ => "",
                },
            };
            TaskDayChoice chosen = choice;
            item.Click += (_, _) => OnChoice(chosen);
            menu.Items.Add(item);
            if (choice.IsSelected) selected ??= item;

            if (choice.Kind == TaskDayChoiceKind.Proposed)
                menu.Items.Add(new MenuFlyoutSeparator());
        }

        // The proposed day is the first line and holds the focus when the menu
        // opens: Enter takes it (spec, Adding a task). Programmatic, not
        // Keyboard: Windows then draws its focus rectangle only for a person
        // who opened the menu from the keyboard. Under a pointer the heavy
        // rectangle read as a selected state the menu does not have.
        if (selected is not null)
            menu.Opened += (_, _) => selected.Focus(FocusState.Programmatic);
        return menu;
    }

    private void OnChoice(TaskDayChoice choice)
    {
        switch (choice.Kind)
        {
            case TaskDayChoiceKind.PickADay:
                ShowCalendar();
                break;
            case TaskDayChoiceKind.NoDay:
                DayChosen?.Invoke(null);
                break;
            default:
                DayChosen?.Invoke(choice.Day);
                break;
        }
    }

    // Pick a day opens the calendar Windows draws, from today on. The calendar
    // fills its flyout, as the one CalendarDatePicker opens does: the flyout
    // is the frame, so the calendar wears none of its own and the presenter
    // keeps no padding around it. Left as they come, the two frames stood one
    // inside the other with a 16 px band between them.
    // For the self-check: the calendar as Pick a day opens it, drawn inside
    // the window where a window capture sees it.
    public void OpenCalendar(bool constrainToWindow = false)
    {
        ShowCalendar();
        if (constrainToWindow && _calendarFlyout is { ShouldConstrainToRootBounds: false } flyout)
        {
            flyout.Hide();
            MailWindow.TryConstrain(() => flyout.ShouldConstrainToRootBounds = true);
            flyout.ShowAt(AddTaskButton);
        }
    }

    private void ShowCalendar()
    {
        var calendar = new CalendarView
        {
            SelectionMode = CalendarViewSelectionMode.Single,
            MinDate = new DateTimeOffset(_today.ToDateTime(TimeOnly.MinValue)),
            FirstDayOfWeek = Windows.Globalization.DayOfWeek.Monday,
            BorderThickness = new Thickness(0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        calendar.SetDisplayDate(new DateTimeOffset((_proposed ?? _today).ToDateTime(TimeOnly.MinValue)));
        calendar.SelectedDatesChanged += (sender, args) =>
        {
            if (args.AddedDates.Count == 0) return;
            _calendarFlyout?.Hide();
            DayChosen?.Invoke(DateOnly.FromDateTime(args.AddedDates[0].DateTime));
        };
        var presenter = new Style(typeof(FlyoutPresenter))
        {
            BasedOn = Application.Current.Resources["DefaultFlyoutPresenterStyle"] as Style,
        };
        presenter.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        _calendarFlyout = ComponentMotion.Still(new Flyout
        {
            Content = calendar,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            FlyoutPresenterStyle = presenter,
        });
        _calendarFlyout.ShowAt(AddTaskButton);
    }
}
