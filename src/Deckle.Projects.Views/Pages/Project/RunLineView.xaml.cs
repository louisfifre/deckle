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
    }

    // What the line says, for the self-check and a screen reader.
    public string Text => LineText.Text;

    public RunLineKind? Kind { get; private set; }

    public void Apply(RunLine? line)
    {
        Kind = line?.Kind;
        LineText.Text = line?.Text ?? "";

        bool running = line?.Kind == RunLineKind.Running;
        Ring.IsActive = running;
        Ring.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        WarningIcon.Visibility = line?.Kind == RunLineKind.Failure ? Visibility.Visible : Visibility.Collapsed;

        bool hasNote = !string.IsNullOrEmpty(line?.Note);
        NoteText.Text = line?.Note ?? "";
        NoteText.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
    }
}
