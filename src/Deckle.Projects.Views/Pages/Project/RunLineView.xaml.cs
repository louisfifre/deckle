using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// Draws a RunLine (the engine's words and kind) without parsing the words: the
// kind picks the leading mark. Nothing is subscribed here; the owner calls
// Apply again whenever the board or the clock moves.
internal sealed partial class RunLineView : UserControl
{
    public RunLineView()
    {
        InitializeComponent();
        WarningIcon.Glyph = ProjectsGlyphs.Warning;
        ToolTipService.SetToolTip(Ring, ProjectsText.Get("Card_Running"));
        AutomationProperties.SetName(Ring, ProjectsText.Get("Card_Running"));
        AutomationProperties.SetAccessibilityView(WarningIcon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

        // The line is single-line and trimmed: its tooltip carries the whole
        // text when the ellipsis is drawn.
        TrimmedTextToolTip.Attach(LineText);

        TerminalIcon.Glyph = ProjectsGlyphs.OpenInNew;
        string show = ProjectsText.Get("Pinned_ShowTerminal");
        ToolTipService.SetToolTip(TerminalButton, show);
        AutomationProperties.SetName(TerminalButton, show);
    }

    // The small "Show terminal" button after the words was pressed.
    public event Action? ShowTerminalRequested;

    // Offers "Show terminal" next to the words of an ended run whose terminal
    // window is still open. The owner decides, since only it knows the run.
    public void SetShowTerminal(bool visible) =>
        TerminalButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnTerminalClick(object sender, RoutedEventArgs e) => ShowTerminalRequested?.Invoke();

    // What the line says, for the self-check and a screen reader.
    public string Text => LineText.Text;

    public RunLineKind? Kind { get; private set; }

    // Whether Apply has been called, so the first line a view draws (a page that
    // opens on a run in progress) is not announced as news.
    private bool _applied;

    public void Apply(RunLine? line)
    {
        RunLineKind? before = Kind;
        Kind = line?.Kind;
        LineText.Text = line?.Text ?? "";

        bool running = line?.Kind == RunLineKind.Running;
        Ring.IsActive = running;
        Ring.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        WarningIcon.Visibility = line?.Kind == RunLineKind.Failure ? Visibility.Visible : Visibility.Collapsed;

        bool hasNote = !string.IsNullOrEmpty(line?.Note);
        NoteText.Text = line?.Note ?? "";
        NoteText.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;

        // A change of state is said once ("Running", "Failed ..."). The text is
        // not a live region: it carries the seconds that count, and a live
        // region would read them out every second.
        if (_applied && line is not null && Kind != before)
        {
            Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(LineText)?.RaiseNotificationEvent(
                Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationKind.ActionCompleted,
                Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.MostRecent,
                line.Text,
                "ProjectsRunLine");
        }
        _applied = true;
    }
}
